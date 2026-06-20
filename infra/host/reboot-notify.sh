#!/usr/bin/env bash
# Daily check (via reboot-notify.timer): if the host needs a reboot to finish
# applying updates, post to a Slack incoming webhook. dnf-automatic installs
# patches but never reboots, so this is how a pending reboot gets noticed.
#
# The webhook URL comes from /etc/reboot-notify.env (WEBHOOK_URL=...), kept out
# of this script so the secret isn't committed. For Discord, change the JSON
# key from "text" to "content"; for ntfy, send "$msg" as a plain-text body.
set -euo pipefail

[ -n "${WEBHOOK_URL:-}" ] || { echo "WEBHOOK_URL not set in /etc/reboot-notify.env" >&2; exit 0; }

# `needs-restarting -r` exits 0 when no reboot is needed, 1 when one is.
if needs-restarting -r >/dev/null 2>&1; then
    exit 0
fi

msg="⚠ $(hostname): a reboot is required to finish applying security updates (kernel/glibc/systemd). Run 'sudo reboot' when convenient."
curl -fsS -X POST -H 'Content-Type: application/json' \
    --data "{\"text\": \"${msg}\"}" "$WEBHOOK_URL" >/dev/null
