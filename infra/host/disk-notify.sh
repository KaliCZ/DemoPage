#!/usr/bin/env bash
set -euo pipefail

[ -n "${WEBHOOK_URL:-}" ] || { echo "WEBHOOK_URL not set in /etc/slack-notify.env" >&2; exit 0; }

THRESHOLD=${DISK_THRESHOLD:-85}   # percent used
STATE=/var/lib/disk-notify/last-notified
THROTTLE=$((24 * 3600))           # seconds between repeat pings while a disk stays over threshold

# Pseudo/overlay filesystems either aren't real storage or are double-counted
# against their backing fs (container overlays), so leave them out.
PSEUDO='-x tmpfs -x devtmpfs -x squashfs -x overlay -x efivarfs'

# Blocks and inodes can each exhaust a filesystem independently; -P keeps df's columns identical for both.
scan() {  # $1 = extra df flags (h | ih), $2 = label
    df -P"$1" $PSEUDO | awk -v t="$THRESHOLD" -v kind="$2" '
        NR > 1 { p = $5; sub(/%/, "", p)
                 if (p + 0 >= t) printf "  %s%% %s used (%s free) on %s\n", p + 0, kind, $4, $6 }'
}

# $(...) strips trailing newlines, so re-add one after each section.
offenders=""
for section in "$(scan h space)" "$(scan ih inodes)"; do
    [ -n "$section" ] && offenders="${offenders}${section}"$'\n'
done

if [ -z "$offenders" ]; then
    rm -f "$STATE"        # back under threshold — reset so the next episode pings immediately
    exit 0
fi

if [ -e "$STATE" ]; then
    age=$(( $(date +%s) - $(stat -c %Y "$STATE") ))
    [ "$age" -lt "$THROTTLE" ] && exit 0
fi

msg="⚠ $(hostname): disk usage has reached ${THRESHOLD}% —
${offenders}Free space soon (e.g. 'podman image prune -a', 'journalctl --vacuum-time=7d') to avoid the stack failing."

# Slack wants a JSON string: escape backslashes and quotes, join lines with \n.
payload=$(printf '%s' "$msg" | awk 'BEGIN{ORS="\\n"} {gsub(/\\/,"\\\\"); gsub(/"/,"\\\""); print}')
curl -fsS -X POST -H 'Content-Type: application/json' \
    --data "{\"text\": \"${payload}\"}" "$WEBHOOK_URL" >/dev/null

mkdir -p "$(dirname "$STATE")"
touch "$STATE"
