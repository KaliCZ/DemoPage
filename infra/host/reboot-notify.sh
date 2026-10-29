#!/usr/bin/env bash
# dnf-automatic installs patches but never reboots, so this is how a pending reboot gets noticed.
set -euo pipefail

[ -n "${WEBHOOK_URL:-}" ] || { echo "WEBHOOK_URL not set in /etc/slack-notify.env" >&2; exit 0; }

STATE=/var/lib/reboot-notify/last-notified
THROTTLE=$((24 * 3600))   # seconds between repeat pings while a reboot stays pending

# `needs-restarting -r` exits 0 when no reboot is needed, 1 when one is.
if needs-restarting -r >/dev/null 2>&1; then
    rm -f "$STATE"        # nothing pending — reset so the next episode pings immediately
    exit 0
fi

if [ -e "$STATE" ]; then
    age=$(( $(date +%s) - $(stat -c %Y "$STATE") ))
    [ "$age" -lt "$THROTTLE" ] && exit 0
fi

msg="⚠ $(hostname): a reboot is required to finish applying security updates (kernel/glibc/systemd). Run 'sudo reboot' when convenient."
curl -fsS -X POST -H 'Content-Type: application/json' \
    --data "{\"text\": \"${msg}\"}" "$WEBHOOK_URL" >/dev/null

mkdir -p "$(dirname "$STATE")"
touch "$STATE"
