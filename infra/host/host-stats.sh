#!/usr/bin/env bash
# Spike counts assume sysstat samples every minute (see infra/host/monitoring.md).
# The host clock must be UTC so the two-file split around midnight is always exactly 24h.
# Slack webhooks are single-channel, so the digest posts to its own STATS_WEBHOOK_URL.
set -euo pipefail

[ -n "${STATS_WEBHOOK_URL:-}" ] || { echo "STATS_WEBHOOK_URL not set in /etc/slack-notify.env" >&2; exit 0; }

SPIKE=${CPU_SPIKE_PCT:-70}          # a sample busier than this counts as a "spike" minute
KEEP_DAYS=${STATS_KEEP_DAYS:-3}     # days of sysstat history to retain after the digest
FRESH_MAX=$(( 2 * 24 * 3600 ))      # a data file older than this is treated as stale

# Locale-independent sar output: one-token timestamps and '.' decimals for the parser below.
export S_TIME_FORMAT=ISO LC_ALL=C

BOUNDARY=$(date +%H:%M:00)            # window edge: now, split point between the two days
YDAY=/var/log/sa/sa$(date -d yesterday +%d)
TODAY=/var/log/sa/sa$(date +%d)

# Guards against a dead collector and last month's saNN reused under the same name.
fresh() { [ -f "$1" ] && [ $(( $(date +%s) - $(stat -c %Y "$1") )) -le "$FRESH_MAX" ]; }

# -e is explicit because sar otherwise defaults it to 18:00, which would drop the evening.
sar_window() {  # $1 = sar flag (-u|-r)
    if fresh "$YDAY";  then sar "$1" -s "$BOUNDARY" -e 23:59:59 -f "$YDAY"  2>/dev/null || true; fi
    if fresh "$TODAY"; then sar "$1" -s 00:00:00 -e "$BOUNDARY" -f "$TODAY" 2>/dev/null || true; fi
}

# Prints "avg max spikes samples", or nothing without data; finds the column by header name so sysstat layout changes don't matter.
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
cores=$(nproc 2>/dev/null || echo '?')
disk=$(df -Ph -x tmpfs -x devtmpfs -x squashfs -x overlay -x efivarfs |
    awk 'NR > 1 { printf "  %s on %s (%s used of %s, %s free)\n", $5, $6, $3, $2, $4 }')

hm=${BOUNDARY%:*}   # HH:MM
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

# Pruned by age, so there's no month-length math and last month's reused saNN goes too.
find /var/log/sa -maxdepth 1 -type f -name 'sa*' -mtime +"$KEEP_DAYS" -delete 2>/dev/null || true
