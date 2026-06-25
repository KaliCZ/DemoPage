#!/usr/bin/env bash
# Run on a timer (disk-notify.timer): post to Slack when any local filesystem
# crosses a usage threshold. The host pulls a lot of container images, so this
# is the early warning before the disk fills and podman/Caddy start failing.
#
# The webhook URL comes from /etc/slack-notify.env (WEBHOOK_URL=...), shared
# with the other host alerters so there's one secret to manage.
#
# Like reboot-notify, the timer checks often and this script throttles the
# *notifications* to one per THROTTLE window so a lingering full disk doesn't
# spam the channel. The marker is cleared once usage drops back under the
# threshold, so the next episode pings immediately.
set -euo pipefail

[ -n "${WEBHOOK_URL:-}" ] || { echo "WEBHOOK_URL not set in /etc/slack-notify.env" >&2; exit 0; }

THRESHOLD=${DISK_THRESHOLD:-85}   # percent used; alert when a filesystem reaches this
STATE=/var/lib/disk-notify/last-notified
THROTTLE=$((24 * 3600))           # seconds between repeat pings while a disk stays over threshold

# Pseudo/overlay filesystems either aren't real storage or are double-counted
# against their backing fs (container overlays), so leave them out.
PSEUDO='-x tmpfs -x devtmpfs -x squashfs -x overlay -x efivarfs'

# Blocks and inodes can each independently exhaust a filesystem; check both.
# -P keeps the columns stable across both reports: $4 free, $5 used%, $6 mount.
scan() {  # $1 = extra df flags (h | ih), $2 = label
    df -P"$1" $PSEUDO | awk -v t="$THRESHOLD" -v kind="$2" '
        NR > 1 { p = $5; sub(/%/, "", p)
                 if (p + 0 >= t) printf "  %s%% %s used (%s free) on %s\n", p + 0, kind, $4, $6 }'
}

# $(...) strips trailing newlines, so collect each section and re-add a newline
# after it — otherwise the rows (and the closing line below) run together.
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
