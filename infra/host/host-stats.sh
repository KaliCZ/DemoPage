#!/usr/bin/env bash
# Run daily (host-stats.timer): post a host health digest to Slack — for the
# previous day, the average / peak / number of busy intervals for CPU and
# memory (from sysstat/sar), plus current disk usage. disk-notify.sh is the
# urgent threshold watchdog; this is the trend digest.
#
# The "N intervals above X%" spike count is the point of per-minute sampling:
# each minute sar records is one interval, so counting the busy ones surfaces a
# local spike that a daily average would smooth away. Set the collection cadence
# to match (see infra/host/monitoring.md).
#
# sysstat names its files by day-of-month (saNN), so a stale saNN can be last
# month's file under the same name. Two guards: we only trust a file written
# recently (FRESH_MAX) — so a dead collector reads as "n/a", never as month-old
# numbers dressed up as current — and after sending we prune to a few days of
# history by file age, which needs no month-length arithmetic.
#
# The digest posts via STATS_WEBHOOK_URL — its own channel, separate from the
# alerts' WEBHOOK_URL. A Slack incoming webhook is one-channel, so the two
# channels are two webhook URLs (same app) in /etc/slack-notify.env.
set -euo pipefail

[ -n "${STATS_WEBHOOK_URL:-}" ] || { echo "STATS_WEBHOOK_URL not set in /etc/slack-notify.env" >&2; exit 0; }

SPIKE=${CPU_SPIKE_PCT:-70}          # a sample busier than this counts as a "spike" minute
KEEP_DAYS=${STATS_KEEP_DAYS:-3}     # days of sysstat history to retain after the digest
FRESH_MAX=$(( 2 * 24 * 3600 ))      # a data file older than this is treated as stale

# ISO time keeps each row's leading timestamp a single token; the row parser
# below maps columns by header name regardless, but this keeps output stable.
export S_TIME_FORMAT=ISO LC_ALL=C

# Prefer yesterday's complete file, fall back to today's partial — but only if
# the file was actually written recently (guards against a dead collector and
# against last month's saNN being reused under the same name).
f=""
for d in "$(date -d yesterday +%d)" "$(date +%d)"; do
    cand="/var/log/sa/sa$d"
    [ -f "$cand" ] || continue
    age=$(( $(date +%s) - $(stat -c %Y "$cand") ))
    [ "$age" -le "$FRESH_MAX" ] && { f="$cand"; break; }
done

# Reduce one sar report to "avg max spikes samples" across its per-interval rows.
# The column is located by header name, so layout differences between sysstat
# versions don't matter; invert=1 turns %idle into busy% (100 - idle). Prints
# nothing when there are no data rows, so the caller can fall back to "n/a".
stats() {  # $1 = sar flag (-u|-r), $2 = column header, $3 = invert (0|1), $4 = spike threshold
    { sar "$1" -f "$f" 2>/dev/null || true; } | awk -v want="$2" -v inv="$3" -v thr="$4" '
        /^Average:/ || /RESTART/ { next }
        index($0, want) && !col  { for (i = 1; i <= NF; i++) if ($i == want) col = i; next }
        col && $col ~ /^[0-9.]+$/ {
            v = inv ? 100 - $col : $col + 0
            n++; sum += v
            if (v > max)   max = v
            if (v > thr)   spikes++
        }
        END { if (n) printf "%.0f %.0f %d %d", sum / n, max, spikes, n }'
}

cpu="n/a"; mem="n/a"; note=""
if [ -z "$f" ]; then
    note="
⚠ no recent sysstat data — is collection running? (systemctl status sysstat-collect.timer)"
else
    if read -r avg max spikes n <<<"$(stats -u %idle 1 "$SPIKE")" && [ -n "${n:-}" ]; then
        cpu="avg ${avg}% · peak ${max}% · ${spikes}/${n} min above ${SPIKE}%"
    fi
    if read -r avg max _ n <<<"$(stats -r %memused 0 "$SPIKE")" && [ -n "${n:-}" ]; then
        mem="avg ${avg}% · peak ${max}%"
    fi
fi

load=$(awk '{printf "%s / %s / %s", $1, $2, $3}' /proc/loadavg)
cores=$(nproc 2>/dev/null || echo '?')   # load == cores is full saturation, so it's the scale for the numbers above
disk=$(df -Ph -x tmpfs -x devtmpfs -x squashfs -x overlay -x efivarfs |
    awk 'NR > 1 { printf "  %s on %s (%s used of %s, %s free)\n", $5, $6, $3, $2, $4 }')

msg="📊 $(hostname) — daily health for $(date -d yesterday +%F)
• CPU: ${cpu}
• Memory: ${mem}
• Load (1/5/15m): ${load}  (${cores} cores = full)
• Disk:
${disk}${note}"

# Slack wants a JSON string: escape backslashes and quotes, join lines with \n.
payload=$(printf '%s' "$msg" | awk 'BEGIN{ORS="\\n"} {gsub(/\\/,"\\\\"); gsub(/"/,"\\\""); print}')
curl -fsS -X POST -H 'Content-Type: application/json' \
    --data "{\"text\": \"${payload}\"}" "$STATS_WEBHOOK_URL" >/dev/null

# Reported — now prune old history. Age-based, so there's no day-of-month or
# month-length math; stale files (incl. last month's under a reused name) go too.
find /var/log/sa -maxdepth 1 -type f -name 'sa*' -mtime +"$KEEP_DAYS" -delete 2>/dev/null || true
