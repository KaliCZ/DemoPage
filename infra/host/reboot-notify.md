# Knowing when the host needs a reboot

`dnf-automatic` installs security patches but **never reboots** (see
[SETUP.md §3.2](../docs/SETUP.md#32-oracle-cloud-vm)). Some updates — kernel,
glibc, systemd, openssl — only take effect after a reboot, so you need to know
when one is pending and do it yourself at a convenient time.

The check is `needs-restarting -r` (from `dnf-utils`), which exits non-zero when
a reboot is required:

```bash
sudo dnf install -y dnf-utils
needs-restarting -r        # "Reboot is required ..." → time to reboot
```

Two optional, additive ways to surface that automatically. Neither affects the
running stack; install whichever you want, whenever.

## A. Login banner (every SSH session)

Prints a warning on login while a reboot is pending:

```bash
sudo tee /etc/profile.d/reboot-required.sh > /dev/null << 'EOF'
if command -v needs-restarting >/dev/null 2>&1; then
    needs-restarting -r >/dev/null 2>&1
    if [ "$?" -eq 1 ]; then
        echo ""
        echo "  ⚠  A reboot is required to finish applying updates (kernel/glibc/systemd)."
        echo "     Details: needs-restarting -r    Reboot when convenient: sudo reboot"
        echo ""
    fi
fi
EOF
```

`/etc/profile.d/` scripts run for **login** shells only (a fresh SSH session) —
not subshells or tmux panes, which is exactly what you want here. The stack
comes back on its own after a reboot (see
[SETUP.md → Reboot Survival](../docs/SETUP.md#reboot-survival)).

## B. Push the alert to Slack

The banner only helps once you log in. To get *pushed* a message when a reboot
becomes due, create a Slack webhook and install the timer from this directory.

First, create the webhook (one-time, in the Slack UI):

1. <https://api.slack.com/apps> → **Create New App** → *From scratch* (or reuse an app).
2. **Incoming Webhooks** → toggle **On** → **Add New Webhook to Workspace** → pick the channel.
3. Copy the **Webhook URL** (`https://hooks.slack.com/services/…`).

Then install ([`reboot-notify.sh`](reboot-notify.sh),
[`.service`](reboot-notify.service), [`.timer`](reboot-notify.timer) — copy from
a checkout, or paste their contents):

```bash
sudo cp infra/host/reboot-notify.sh /usr/local/bin/reboot-notify.sh
sudo chmod +x /usr/local/bin/reboot-notify.sh
sudo cp infra/host/reboot-notify.service infra/host/reboot-notify.timer /etc/systemd/system/

# Paste the webhook URL from step 3 above
printf 'WEBHOOK_URL=%s\n' 'https://hooks.slack.com/services/XXX/YYY/ZZZ' | sudo tee /etc/reboot-notify.env >/dev/null
sudo chmod 600 /etc/reboot-notify.env

sudo systemctl daemon-reload
sudo systemctl enable --now reboot-notify.timer
```

These are **system** units (under `/etc/systemd/system/`, run by PID 1), so they
need no linger — just `enable` to start on boot. Verify the webhook end-to-end:

```bash
sudo bash -c 'source /etc/reboot-notify.env && curl -fsS -X POST -H "Content-Type: application/json" \
  --data "{\"text\":\"✅ reboot-notify test from $(hostname)\"}" "$WEBHOOK_URL"'
```

### Behavior

The timer **checks** every 15 minutes; the script **notifies** at most once per
24h while a reboot stays pending:

| When | Result |
|------|--------|
| No reboot pending | silent; marker (`/var/lib/reboot-notify/last-notified`) cleared |
| Reboot first becomes pending | pings immediately (next 15-min tick), records timestamp |
| Still pending, < 24h since last ping | silent |
| Still pending, ≥ 24h since last ping | pings again, resets the 24h clock |
| You reboot (pending clears) | marker removed → a future episode pings right away |

So: fast detection, one ping up front, at most a daily re-nag until you act.

### Tuning

- **Check cadence** — edit `OnCalendar=` in [`reboot-notify.timer`](reboot-notify.timer) (`*:0/15`, `hourly`, `daily`, …).
- **Re-nag interval** — edit `THROTTLE` in [`reboot-notify.sh`](reboot-notify.sh) (seconds).

After editing either, re-copy the file and `sudo systemctl daemon-reload && sudo systemctl restart reboot-notify.timer`.

> **Why Slack, not email?** A Slack incoming webhook is one URL you `curl` over
> 443. Email from the VM is harder: OCI blocks outbound port 25 by default and a
> fresh box has no mailer, so you'd first have to configure postfix/msmtp
> against an external SMTP relay before `dnf-automatic`'s `emit_via = email`
> could send anything. For Discord, change the JSON key in the script from
> `text` to `content`.
