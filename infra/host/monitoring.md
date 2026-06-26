# Host monitoring: disk alerts and health stats

Two Slack alerters for the Oracle Linux VM, plus the historical CPU/memory data
behind them. Like the [OS-update notifier](os-updates.md), these are additive
host ops — none of it touches the running app stack.

- **`disk-notify`** — a watchdog that pings Slack when any filesystem crosses a
  usage threshold (the early warning before the disk fills from container
  images and podman/Caddy start failing).
- **`host-stats`** — a daily digest posting the last 24h of CPU and memory
  (average, peak, and how many minutes ran hot), plus current disk usage.

Both read `/etc/slack-notify.env`, alongside the OS-update notifier. It holds
two webhook URLs so urgent alerts and the daily digest land in separate channels
(a Slack incoming webhook is bound to one channel — see
[its own channel](#posting-the-digest-to-its-own-channel)):

- `WEBHOOK_URL` — alerts channel: `reboot-notify` and `disk-notify`.
- `STATS_WEBHOOK_URL` — stats channel: the `host-stats` digest.

If you haven't created the file and the first (`WEBHOOK_URL`) webhook yet, see
[os-updates.md → Push the alert to Slack](os-updates.md#b-push-the-alert-to-slack).

> **Updating a host with no repo checkout.** The `cp` steps below assume you're
> in a checkout, as at first install — CI never ships these host scripts (it
> deploys only the app slot units). To update one in place on the VM, copy it up
> and install it with `sudo`:
>
> ```bash
> scp infra/host/host-stats.sh opc@<public-ip>:/tmp/
> ssh opc@<public-ip> 'sed -i "s/\r$//" /tmp/host-stats.sh && sudo install -m 755 /tmp/host-stats.sh /usr/local/bin/host-stats.sh'
> ```
>
> The `sed` strips CR in case the file was checked out on Windows (a CRLF shebang
> breaks the script). Then `daemon-reload`/restart as the section below shows.

## Disk-space alert (`disk-notify`)

Install ([`disk-notify.sh`](disk-notify.sh), [`.service`](disk-notify.service),
[`.timer`](disk-notify.timer)):

```bash
sudo cp infra/host/disk-notify.sh /usr/local/bin/disk-notify.sh
sudo chmod +x /usr/local/bin/disk-notify.sh
sudo cp infra/host/disk-notify.service infra/host/disk-notify.timer /etc/systemd/system/

sudo systemctl daemon-reload
sudo systemctl enable --now disk-notify.timer
```

The timer **checks** hourly; the script **notifies** at most once per 24h while a
filesystem stays over threshold (default **85%**), for both block and inode
usage. The marker (`/var/lib/disk-notify/last-notified`) is cleared once usage
drops back under, so a fresh episode pings right away — same shape as
[reboot-notify](os-updates.md#behavior).

Force a run to confirm it's wired up (silent if everything's under threshold):

```bash
sudo systemctl start disk-notify.service && journalctl -u disk-notify.service -n 20 --no-pager
```

### Growing the boot volume

When the watchdog says the disk is filling, this is how you add space. On OCI the
boot volume can be enlarged in the console, but that **does not** resize the OS
filesystem — the extra space sits unallocated until you grow the partition and
LVM volume too. (Tell-tale: the console shows e.g. 150GB but `df` shows ~30GB on
`/`, and `lsblk` shows `sda` larger than the sum of its partitions.)

The VM uses Oracle Linux's stock LVM layout — one disk (`sda`), an LVM partition
(`sda3`) in volume group `ocivolume`, split into `root` (mounted `/`) and `oled`
(reserved, `/var/oled`). **Container images and volumes live on `/`** (rootless
podman under `/home/opc`), so `root` is the volume to grow.

Resize the boot volume in the OCI console first, then on the host confirm the
layout and grow `/` into the new space — online, no reboot, no data loss:

```bash
lsblk                                                 # confirm sda is now bigger than its partitions
sudo growpart /dev/sda 3                              # grow the partition to fill the disk
sudo pvresize /dev/sda3                               # let LVM see the bigger partition
sudo lvextend -r -l +100%FREE /dev/ocivolume/root     # extend the root LV + grow its XFS filesystem
df -h /                                               # verify the new size
```

`lvextend` names `/dev/ocivolume/root` explicitly, so only `/` grows — `oled` is
untouched. XFS can only grow, never shrink, so snapshot/back up the boot volume
in the console first if you want a safety net. If `growpart` is missing:
`sudo dnf install -y cloud-utils-growpart`.

## Daily health digest (`host-stats`)

For each of CPU and memory the digest reports **average**, **peak**, and (for
CPU) **how many intervals ran above a threshold** (`CPU_SPIKE_PCT`, default 70%)
over the **trailing 24h** ending when it runs (so the digest is current at send
time, not hours stale). That spike count is what catches a local spike a daily
average would smooth away — but it's only meaningful if each interval is short,
so the collection cadence matters.

`sysstat` is the standard tool for this history. Each `sar` sample stores the
kernel's cumulative counters; the per-interval CPU% is the *average over that
interval* (counter delta ÷ time), so nothing between samples is lost. Memory is
an instantaneous gauge, so its samples are point-in-time.

```bash
sudo dnf install -y sysstat
sudo systemctl enable --now sysstat-collect.timer sysstat-summary.timer
```

The default cadence is every 10 minutes; drop it to **1 minute** so the spike
count has the resolution to mean "a busy minute". (1 min matches systemd timers'
default `AccuracySec`; finer than that needs extra tuning for little gain.)

```bash
sudo mkdir -p /etc/systemd/system/sysstat-collect.timer.d
sudo tee /etc/systemd/system/sysstat-collect.timer.d/override.conf >/dev/null << 'EOF'
[Timer]
OnCalendar=
OnCalendar=minutely
AccuracySec=1s
EOF
sudo systemctl daemon-reload
sudo systemctl restart sysstat-collect.timer
```

> **Storage & staleness:** the digest prunes `/var/log/sa/` to the last
> `STATS_KEEP_DAYS` (default **3**) after each run, by file age — so 1-minute
> sampling stays at single-digit MB and never accumulates. This also closes a
> trap: sysstat names files by day-of-month (`saNN`), so a dead collector would
> otherwise leave last month's `saNN` in place to be misread as "today". The
> digest only trusts a file written in the last ~2 days; a stale or missing one
> reads as `n/a` with a "collection may have stopped" note, never as old numbers
> dressed up as current.

> Give it a few sampling intervals before the first digest has data — until a
> recent `/var/log/sa/saNN` file exists, the CPU/memory fields read `n/a` (disk
> usage still works, since that's read live).

Then install the digest ([`host-stats.sh`](host-stats.sh),
[`.service`](host-stats.service), [`.timer`](host-stats.timer)):

```bash
sudo cp infra/host/host-stats.sh /usr/local/bin/host-stats.sh
sudo chmod +x /usr/local/bin/host-stats.sh
sudo cp infra/host/host-stats.service infra/host/host-stats.timer /etc/systemd/system/

sudo systemctl daemon-reload
sudo systemctl enable --now host-stats.timer
```

It fires daily at 09:00 and reports the **trailing 24h** ending at that time
(09:00 yesterday → 09:00 today). The host runs on UTC, so that window is a clean,
continuous 24h with no DST seam. First add its webhook
([below](#posting-the-digest-to-its-own-channel)) — the digest needs
`STATS_WEBHOOK_URL` — then send one now to verify:

```bash
sudo systemctl start host-stats.service
```

### Posting the digest to its own channel

The digest requires its own `STATS_WEBHOOK_URL` — it does **not** fall back to
the alerts channel, so a missing one is a clear config error rather than daily
stats quietly flooding the alerts channel. Add a **second** Incoming Webhook to
the *same* Slack app (a webhook is bound to one channel, so a different channel
needs a different URL — but not a different app), then add it to the env file:

```bash
printf 'STATS_WEBHOOK_URL=%s\n' 'https://hooks.slack.com/services/AAA/BBB/CCC' | sudo tee -a /etc/slack-notify.env >/dev/null
```

`disk-notify` and `reboot-notify` use `WEBHOOK_URL`; only the digest uses
`STATS_WEBHOOK_URL`.

## Querying the stats ad-hoc

`sysstat` data is also queryable directly with `sar` any time — useful when an
alert fires and you want detail the digest doesn't carry:

```bash
sar -u                       # CPU, every sample today (last line is the average)
sar -r                       # memory
sar -u -f /var/log/sa/sa15   # a specific day-of-month (here, the 15th)
sar -d -p                    # per-disk I/O
sar -q                       # load average + run queue
```

The digest prunes history to the last few days (`STATS_KEEP_DAYS`, see Tuning),
so `sar -f` lookback only reaches back that far. sysstat's own `HISTORY`
(`/etc/sysconfig/sysstat`) is moot once our prune runs tighter than it.

## Tuning

- **Disk threshold** — set `DISK_THRESHOLD=` (percent) in `/etc/slack-notify.env`;
  `disk-notify.service` already sources that file.
- **Check / re-nag cadence** — `OnCalendar=` in [`disk-notify.timer`](disk-notify.timer)
  and `THROTTLE` in [`disk-notify.sh`](disk-notify.sh).
- **Digest time** — `OnCalendar=` in [`host-stats.timer`](host-stats.timer).
- **CPU spike threshold** — set `CPU_SPIKE_PCT=` (percent) in `/etc/slack-notify.env`;
  the digest counts intervals busier than this.
- **Sampling resolution** — `OnCalendar=` in the `sysstat-collect.timer` drop-in
  above; finer samples sharpen the spike count at the cost of more stored data.
- **History retention** — set `STATS_KEEP_DAYS=` in `/etc/slack-notify.env`; the
  digest prunes `/var/log/sa/` to that many days (by file age) after each run.
- **Digest channel** — `STATS_WEBHOOK_URL=` in `/etc/slack-notify.env` is the
  digest's own channel (separate from the alerts' `WEBHOOK_URL`; see above).

After editing a unit or script, re-copy it (or stream it up if the VM has no
checkout — see the note under [Disk-space alert](#disk-space-alert-disk-notify))
and `sudo systemctl daemon-reload && sudo systemctl restart <name>.timer`.
