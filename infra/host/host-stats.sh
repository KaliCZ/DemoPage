#!/usr/bin/env bash
# Run daily (host-stats.timer): post a host health digest to Slack — for the
# trailing 24h ending at run time, the average / peak / number of busy intervals
# for CPU and memory (from sysstat/sar), plus current disk usage. disk-notify.sh
# is the urgent threshold watchdog; this is the trend digest.
#
# A rolling window (not the previous calendar day) so the digest is current at
# send time rather than hours stale. The window crosses midnight, so it spans
# two day-of-month files: yesterday's saNN from the boundary to end-of-day, plus
# today's from start-of-day to the boundary. The host runs on UTC, so the wall
# clock is continuous (no DST jumps) and that split is always a clean 24h.
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

BOUNDARY=$(date +%H:%M:00)            # window edge: now, split point between the two days
YDAY=/var/log/sa/sa$(date -d yesterday +%d)
TODAY=/var/log/sa/sa$(date +%d)

# True if the file exists and was written within FRESH_MAX — guards a dead
# collector and last month's saNN reused under the same name.
fresh() { [ -f "$1" ] && [ $(( $(date +%s) - $(stat -c %Y "$1") )) -le "$FRESH_MAX" ]; }

# Emit one sar report's per-interval rows across the trailing 24h: yesterday from
# the boundary to end-of-day, then today up to the boundary. Both -s and -e are
# pinned because sar otherwise defaults -e to 18:00, which would drop the evening.
# A missing or stale segment just contributes no rows.
sar_window() {  # $1 = sar flag (-u|-r)
    if fresh "$YDAY";  then sar "$1" -s "$BOUNDARY" -e 23:59:59 -f "$YDAY"  2>/dev/null || true; fi
    if fresh "$TODAY"; then sar "$1" -s 00:00:00 -e "$BOUNDARY" -f "$TODAY" 2>/dev/null || true; fi
}

# Reduce the windowed sar rows to "avg max spikes samples". The column is located
# by header name, so layout differences between sysstat versions don't matter (and
# the second segment's repeated header is harmlessly ignored once the column is
# found); invert=1 turns %idle into busy% (100 - idle). Prints nothing when there
# are no data rows, so the caller can fall back to "n/a".
stats() {  # $1 = sar flag (-u|-r), $2 = column header, $3 = invert (0|1), $4 = spike threshold
    sar_window "$1" | awk -v want="$2" -v inv="$3" -v thr="$4" '
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
if read -r avg max spikes n <<<"$(stats -u %idle 1 "$SPIKE")" && [ -n "${n:-}" ]; then
    cpu="avg ${avg}% · peak ${max}% · ${spikes}/${n} min above ${SPIKE}%"
else
    note="
⚠ no recent sysstat data — is collection running? (systemctl status sysstat-collect.timer)"
fi
if read -r avg max _ n <<<"$(stats -r %memused 0 "$SPIKE")" && [ -n "${n:-}" ]; then
    mem="avg ${avg}% · peak ${max}%"
fi

load=$(awk '{printf "%s / %s / %s", $1, $2, $3}' /proc/loadavg)
cores=$(nproc 2>/dev/null || echo '?')   # load == cores is full saturation, so it's the scale for the numbers above
disk=$(df -Ph -x tmpfs -x devtmpfs -x squashfs -x overlay -x efivarfs |
    awk 'NR > 1 { printf "  %s on %s (%s used of %s, %s free)\n", $5, $6, $3, $2, $4 }')

hm=${BOUNDARY%:*}   # HH:MM for the header — the window runs hm yesterday → hm today
msg="📊 $(hostname) — health, $(date -d yesterday +%F) ${hm} → $(date +%F) ${hm} $(date +%Z) (last 24h)
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
