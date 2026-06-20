# Setup Guide — kalandra.tech

Step-by-step guide for local development, testing, and deployment infrastructure.

For architecture, tech stack, and decision log, see the [Project page](https://www.kalandra.tech/project).

---

## Table of Contents

- [1. Local Development](#1-local-development)
  - [1.1 Prerequisites](#11-prerequisites)
  - [1.2 Install Dependencies](#12-install-dependencies)
  - [1.3 Start Everything](#13-start-everything)
  - [1.4 Local Supabase](#14-local-supabase)
  - [1.5 Frontend Environment](#15-frontend-environment)
  - [1.6 Stopping Services](#16-stopping-services)
  - [1.7 Parallel Worktrees](#17-parallel-worktrees)
- [2. Running Tests](#2-running-tests)
  - [2.1 Backend Integration Tests](#21-backend-integration-tests)
  - [2.2 Frontend Page Tests](#22-frontend-page-tests)
  - [2.3 E2E Tests](#23-e2e-tests)
- [3. Infrastructure Setup](#3-infrastructure-setup)
  - [3.1 Supabase Project](#31-supabase-project)
  - [3.2 Oracle Cloud VM](#32-oracle-cloud-vm)
    - [Automatic Security Updates](#enable-automatic-security-updates)
    - [Knowing When a Reboot Is Needed](#knowing-when-a-reboot-is-needed)
    - [Reboot Survival](#reboot-survival)
  - [3.3 Shared Caddy Reverse Proxy](#33-shared-caddy-reverse-proxy)
    - [3.3.1 Provision the shared proxy](#331-provision-the-shared-proxy-once-per-machine)
    - [3.3.2 Per-site TLS certificate](#332-per-site-tls-certificate)
    - [3.3.3 Start Caddy](#333-start-caddy)
    - [3.3.4 How apps attach](#334-how-apps-attach)
    - [3.3.5 Configure Cloudflare SSL Mode](#335-configure-cloudflare-ssl-mode)
  - [3.4 Enable IPv6 on the VCN](#34-enable-ipv6-on-the-vcn)
  - [3.5 DNS](#35-dns)
- [4. CI/CD Configuration](#4-cicd-configuration)
  - [4.1 GitHub Repository Secrets](#41-github-repository-secrets)
  - [4.2 GitHub Actions Environment](#42-github-actions-environment)
  - [4.3 Container Registry Auth](#43-container-registry-auth)
- [5. Observability](#5-observability)
  - [5.1 Sentry Projects](#51-sentry-projects)
  - [5.2 Environments and CI noise](#52-environments-and-ci-noise)
  - [5.3 Local Development](#53-local-development)
  - [5.4 Source Maps](#54-source-maps)

---

## 1. Local Development

### 1.1 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL + local Supabase)
- [Node.js 22+](https://nodejs.org/)

### 1.2 Install Dependencies

Run once after cloning (or when dependencies change):

```bash
npm install            # Installs root + frontend dependencies (via postinstall)
```

### 1.3 Start Everything

```bash
npm run aspire   # Installs deps, starts PostgreSQL + local Supabase, then launches the Aspire AppHost
```

The Aspire AppHost orchestrates the API and frontend and exposes the Aspire dashboard with per-resource logs, distributed traces (OpenTelemetry), metrics, and structured logs in one UI. The dashboard and frontend URLs are printed (clickably, in supporting terminals) on startup. Production telemetry is routed to **Sentry** via the OTEL bridge (see [Observability](#observability)); nothing about that changes when running locally under Aspire.

Supabase containers stay owned by the Supabase CLI — `Ctrl+C`-ing the AppHost would otherwise leak them. The AppHost surfaces the API / Studio / Mailpit endpoints on the dashboard as external services (display-only, no lifecycle), so you get one-click access without the cleanup risk. Use `supabase stop` to halt the stack, or `supabase stop --no-backup` to discard data.

Application data lives in a **separate Postgres** owned by Aspire (`AddPostgres`) — not Supabase's bundled DB. Each worktree gets its own container and named volume (`kalandra-pgdata-<repo-folder>`); Ctrl+C stops the container, the volume persists across runs.

### 1.4 Local Supabase

The project includes a `supabase/config.toml` that configures a local Supabase instance with email/password auth (no email confirmation required). On first run, `supabase start` downloads the required Docker images (~2-3 min).

Local services:
| Service | URL |
|---------|-----|
| API gateway | `http://localhost:54321` |
| Studio dashboard | `http://localhost:54323` |
| Inbucket (email) | `http://localhost:54324` |

Local credentials (well-known dev values, not secrets):
- **Publishable key**: `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0`

### 1.5 Frontend Environment

The committed `frontend/.env` has local Supabase defaults — ready to use out of the box.

To override any frontend env vars locally, create `frontend/.env.local` (gitignored). Common overrides:
```
PUBLIC_SUPABASE_URL=https://your-project.supabase.co
PUBLIC_SUPABASE_PUBLISHABLE_KEY=your-publishable-key
PUBLIC_TURNSTILE_SITE_KEY=your-real-site-key
```

> `PUBLIC_API_URL` is intentionally empty in dev — the Aspire AppHost forces it to `""` so all API calls flow through Vite's `/api` proxy (see [`astro.config.mjs`](../frontend/astro.config.mjs)). Setting it in `.env.local` has no effect under `npm run aspire`. It's only meaningful at production build time (e.g. `https://api.kalandra.tech`).

#### Cloudflare Turnstile (CAPTCHA)

The committed `.env` uses Cloudflare's [always-pass test keys](https://developers.cloudflare.com/turnstile/troubleshooting/testing/) so the form works locally without a real Turnstile widget. The backend `appsettings.json` uses the matching always-pass test secret (`1x0000000000000000000000000000000AA`).

To test with a real widget locally, override in `.env.local` (frontend) and user-secrets (backend):
```
# frontend/.env.local
PUBLIC_TURNSTILE_SITE_KEY=your-real-site-key

# backend — via environment variable or appsettings override
Turnstile__SecretKey=your-real-secret-key
```

### 1.6 Stopping Services

Aspire owns the application Postgres — Ctrl+C-ing the AppHost stops the container, but the named volume (`kalandra-pgdata-<repo-folder>`) keeps the data for next time.

Supabase is shared machine-wide and outlives the AppHost. To halt it:

```bash
supabase stop                 # Stop containers (preserves data)
supabase stop --no-backup     # Stop and wipe Supabase state
```

### 1.7 Parallel Worktrees

Just run `npm run aspire` in each. The AppHost walks the dashboard / OTLP ports up from their defaults until it finds free ones, so the first instance is at `15036`, the second at `15037`, etc. dcp handles API and frontend ports the same way internally. The startup output prints clickable URLs for the dashboard and frontend.

The application Postgres is per-worktree (Aspire scopes the data volume to the repo-folder name), so each worktree has its own DB state. Supabase is shared (one machine-level instance), so auth users and storage objects are visible across worktrees — that's fine for fixtures.

---

## 2. Running Tests

```bash
npm test               # Runs all tests: backend + frontend + E2E
```

### 2.1 Backend Integration Tests

Requires Docker (Testcontainers spins up a real PostgreSQL container):

```bash
dotnet test
```

### 2.2 Frontend Page Tests

Builds the static site, serves it, and verifies page rendering, navigation, i18n, and dark mode:

```bash
npm --prefix frontend test  # Installs Playwright browsers automatically
```

### 2.3 E2E Tests

Runs Playwright against the full stack (frontend + backend + DB):

```bash
npm run test:e2e
```

---

## 3. Infrastructure Setup

This section covers the one-time setup needed to host this project yourself.

**Shared host infrastructure vs. app deploy.** §3.2 (the VM, podman, OS
updates, firewall, IPv6) and §3.3 (the shared Caddy proxy) are **shared
infrastructure** — set up once per machine and assumed to already exist by
every app's CI/CD. The box is multi-tenant: demopage and other apps (e.g.
hampap) share the same rootless podman (under `opc`) and the same Caddy.
demopage's pipeline (§4) deploys **only its own containers and its own Caddy
site fragment** — it never installs podman, never owns Caddy, and fails fast if
the shared pieces are missing. The machine is otherwise **stateless**: app data
lives in Supabase / external Postgres and secrets live in GitHub, so the only
on-disk state worth preserving is the Caddy certs under `/srv/caddy/certs`.

### 3.1 Supabase Project

#### Create Project

1. Go to [supabase.com](https://supabase.com) and create a new project
2. Note these values from **Settings → API**:
   - **Project URL** (e.g., `https://abcdef.supabase.co`)
   - **Publishable key** (safe for browser)
   - **JWT keys** — the backend fetches these automatically via JWKS; no manual configuration needed

#### Configure OAuth Provider (Google)

1. In Supabase dashboard: **Authentication → Providers → Google**
2. Enable Google provider
3. Create OAuth credentials in [Google Cloud Console](https://console.cloud.google.com/apis/credentials):
   - Application type: Web application
   - Authorized redirect URI: `https://<your-project-ref>.supabase.co/auth/v1/callback`
4. Paste the Client ID and Client Secret into Supabase
5. Optionally enable GitHub as a second provider (same flow)

#### Configure Redirect URLs

In **Authentication → URL Configuration**:
- **Site URL**: `https://www.kalandra.tech`
- **Redirect URLs** (add all):
  - `https://www.kalandra.tech/**`
  - `https://kalandra.tech/**`
  - `http://localhost:4321/**` (for local development)

#### Create Storage Bucket

1. In Supabase dashboard: **Storage → New bucket**
2. Name: `job-offer-attachments`
3. Public: **off**
4. File size limit: `15 MB`
5. Allowed MIME types: `application/pdf`, `application/msword`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `text/plain`, `image/png`, `image/jpeg`, `image/webp`

> Locally, the bucket is auto-created by `supabase start` from the `[storage.buckets.job-offer-attachments]` section in `supabase/config.toml`.

#### Set Up Admin User

After signing in for the first time, assign the admin role:

```sql
-- Run in Supabase SQL Editor to set admin via app_metadata
UPDATE auth.users
SET raw_app_meta_data = raw_app_meta_data || '{"roles": ["admin"]}'::jsonb
WHERE email = 'your@email.com';
```

### 3.2 Oracle Cloud VM

#### Create the VM

1. Sign up for [Oracle Cloud Free Tier](https://cloud.oracle.com/free)
2. Create a Compute instance:
   - Shape: `VM.Standard.A1.Flex` (ARM, Always Free up to 4 OCPU / 24 GB RAM — size it to what you need; the shape can be resized later from the Console)
   - Image: **Oracle Linux 8** (aarch64 — ARM image for the A1 shape)
   - Add your SSH public key
3. Note the **public IP address**. The default login user is **`opc`** — match the `OCI_USERNAME` GitHub variable (see §4.1).

#### Install podman

Oracle Linux ships rootless `podman` — usually preinstalled. Make sure it's
present (the deploy and the shared Caddy both run rootless under `opc`):

```bash
sudo dnf install -y podman
podman --version
```

Containers run **rootless** under the `opc` user. SELinux is enforcing on Oracle
Linux; podman handles bind-mount relabeling natively via the `:z` flag on the
Quadlet `Volume=` lines, so no manual `chcon`/`semanage` is needed for the
static config. (The deploy additionally `chcon`s each Caddy fragment it writes,
since the `:z` relabel only runs at container start.)

#### Enable automatic security updates

Oracle Linux applies security patches automatically through `dnf-automatic`:

```bash
sudo dnf install -y dnf-automatic
```

Edit `/etc/dnf/automatic.conf`:

```ini
[commands]
upgrade_type = security      # security-only — the conservative choice for a server
apply_updates = yes          # download AND install (not just download)
```

Enable the timer:

```bash
sudo systemctl enable --now dnf-automatic.timer
```

`dnf-automatic` **never reboots the host** — it only installs packages. That's
exactly what we want: no surprise restarts. The catch is that some updates
(kernel, glibc, systemd, openssl) only take effect *after* a reboot, so you need
to know when one is pending and do it yourself at a convenient time.

#### Knowing When a Reboot Is Needed

The check is `needs-restarting -r` (from `dnf-utils`), which exits non-zero and
prints the reason when a reboot is required:

```bash
sudo dnf install -y dnf-utils
needs-restarting -r        # "Reboot is required ..." → time to reboot
```

To surface that automatically, install a login banner so **every SSH session**
tells you:

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

When the banner appears, reboot when convenient — the stack comes back on its
own (see [Reboot Survival](#reboot-survival)).

##### Optional: push the alert to Slack

The banner only helps once you log in. To get *pushed* a message the day a
reboot becomes due, install the daily timer from [`infra/host/`](../infra/host):

```bash
sudo cp infra/host/reboot-notify.sh /usr/local/bin/reboot-notify.sh
sudo chmod +x /usr/local/bin/reboot-notify.sh
sudo cp infra/host/reboot-notify.service infra/host/reboot-notify.timer /etc/systemd/system/

# Slack incoming-webhook URL (Slack → your app → Incoming Webhooks → Add)
printf 'WEBHOOK_URL=%s\n' 'https://hooks.slack.com/services/XXX/YYY/ZZZ' | sudo tee /etc/reboot-notify.env >/dev/null
sudo chmod 600 /etc/reboot-notify.env

sudo systemctl enable --now reboot-notify.timer
```

It runs `needs-restarting -r` once a day and posts to Slack **only** when a
reboot is pending.

> **Why Slack, not email?** A Slack incoming webhook is one URL you `curl` over
> 443. Email from the VM is harder: OCI blocks outbound port 25 by default and a
> fresh box has no mailer, so you'd first have to configure postfix/msmtp
> against an external SMTP relay (provider account + app password on 587) before
> `dnf-automatic`'s `emit_via = email` could send anything. (For Discord instead
> of Slack, change the JSON key in the script from `text` to `content`.)

#### Authenticate to GHCR

The API image is pulled from GitHub Container Registry. Create a GitHub
Personal Access Token with `read:packages` scope and log in once as `opc`
(the deploy pulls rootless):

```bash
echo <GITHUB_PAT> | podman login ghcr.io -u <GITHUB_USERNAME> --password-stdin
```

The credentials are stored under `~/.config/containers/auth.json`, which persists across reboots.

#### Configure firewall

Oracle Linux runs `firewalld`. Open the ports (the API and Caddy use host networking, so they bind these host ports directly):

```bash
sudo firewall-cmd --permanent --add-port=80/tcp
sudo firewall-cmd --permanent --add-port=443/tcp
sudo firewall-cmd --permanent --add-port=8080/tcp
sudo firewall-cmd --reload
```

> If your OCI image manages `iptables` directly rather than running `firewalld`, add the equivalent `INPUT … -j ACCEPT` rules and persist them with `iptables-save` instead.

Also add ingress rules in OCI Console:
- **Networking → Virtual Cloud Networks → Security Lists**
- Add ingress rules for ports 80, 443, 8080

#### API containers (Quadlet + systemd)

The API runs in two slots — `kalandra-api-blue` (port 8080) and
`kalandra-api-green` (port 8081) — managed as rootless `systemd --user` services
via Quadlet, from the unit files in [`infra/quadlet/`](../infra/quadlet). At most
one slot is the active upstream at a time; the CI/CD deploy script swaps slots on
each release. The shared Caddy proxy (§3.3) routes `api.kalandra.tech` to
whichever port the active slot is on.

**Nothing to do here manually** — but the deploy *assumes* §3.2 and §3.3 are
already done, and aborts with a pointer if not. The deploy job syncs the
`.container` files to `~/.config/containers/systemd/`, writes
`~/kalandra-api.env` (the app secrets, read via `EnvironmentFile=`), rewrites
each unit's `Image=` to the **digest** it just built (`…@sha256:…`, never
`:latest`, so a restart can't silently run a different build), reloads Quadlet,
pulls that image, and starts the target slot. It then writes this app's Caddy
fragment (`/srv/caddy/sites/demopage.caddy`) pointing at the active port and
reloads the shared Caddy.

#### Reboot Survival

The stack comes back on its own after **any** restart — a kernel-update reboot
you trigger, a crash, or OCI host maintenance — with no manual step. Three
pieces make that work, all established by host setup / the first deploy:

- **Linger** (`sudo loginctl enable-linger opc`, set in host setup) keeps the `opc` user's `systemd --user` instance running across SSH logouts *and* reboots; without it, `--user` services only exist while someone is logged in.
- **Quadlet `[Install] WantedBy=default.target`** in each `.container` file makes the generated units start on boot — the shared Caddy and both API slots.
- **Persisted on-disk state** — `~/kalandra-api.env` (secrets) and the digest baked into each unit's `Image=`, plus `/srv/caddy/sites/*.caddy` + `/srv/caddy/certs` (routing + TLS) and Caddy's named volumes. So Caddy comes back routing to the last-active slot and the API starts with its secrets and the same pinned image.

On a cold boot both slots start (each on its own port); Caddy routes to whichever the persisted fragment names, and the next deploy reconciles back to one. No traffic is lost.

### 3.3 Shared Caddy Reverse Proxy

Caddy is the HTTPS reverse proxy in front of **every** app on the box
(demopage, hampap, …) — **shared host infrastructure**, set up once per machine
here. After that, each app's CI/CD only drops a site fragment and reloads it;
no app owns Caddy's lifecycle. It runs rootless under `opc` as a Quadlet unit,
like the API slots. TLS uses Cloudflare Origin Certificates (not Let's Encrypt),
since the hostnames are proxied through Cloudflare.

Config lives under `/srv/caddy`, owned by `opc` (rootless Caddy and the app
deploys all run as `opc`, so they must be able to read/write it):

| Path | Purpose |
|------|---------|
| `Caddyfile` | Base config — `import /etc/caddy/sites/*.caddy` ([`infra/caddy/Caddyfile`](../infra/caddy/Caddyfile)) |
| `sites/` | One `<app>.caddy` fragment per app; each app's CI writes its own |
| `certs/` | Cloudflare Origin cert pairs, one per site |

The Quadlet units ([`caddy.container`](../infra/caddy/caddy.container) + the two `.volume` files in [`infra/caddy/`](../infra/caddy)) live in `~/.config/containers/systemd/`.

#### 3.3.1 Provision the shared proxy (once per machine)

```bash
# Config tree, owned by opc so the rootless container and app deploys can use it
sudo install -d -o "$USER" -g "$USER" /srv/caddy /srv/caddy/sites /srv/caddy/certs
cp infra/caddy/Caddyfile /srv/caddy/Caddyfile

# Quadlet units for the shared proxy
mkdir -p ~/.config/containers/systemd
cp infra/caddy/caddy.container infra/caddy/caddy-data.volume infra/caddy/caddy-config.volume \
   ~/.config/containers/systemd/

# Let the rootless container bind 80/443, and keep user services alive across reboots
echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-unprivileged-ports.conf
sudo sysctl --system
sudo loginctl enable-linger "$USER"
```

(If the repo isn't checked out on the VM, paste the file contents from
[`infra/caddy/`](../infra/caddy) instead.)

#### 3.3.2 Per-site TLS certificate

For each hostname Caddy serves (here, `api.kalandra.tech` for demopage):

1. Cloudflare dashboard → **SSL/TLS → Origin Server → Create Certificate**, hostname `api.kalandra.tech`, 15-year validity, PEM format.
2. Save the pair to `/srv/caddy/certs` (owned by `opc`, so no `sudo`), named **per site** so multiple apps don't collide. demopage's CI fragment expects `kalandra.pem` / `kalandra.key`:

```bash
nano /srv/caddy/certs/kalandra.pem    # paste the certificate
nano /srv/caddy/certs/kalandra.key    # paste the private key
chmod 600 /srv/caddy/certs/kalandra.key
```

#### 3.3.3 Start Caddy

```bash
systemctl --user daemon-reload
systemctl --user start caddy.service
```

The `ip_unprivileged_port_start` sysctl (set in §3.3.1) lets the rootless
container bind 80/443; linger + `[Install] WantedBy=default.target` bring Caddy
back on every reboot. An empty `sites/` dir is fine on first start — the import
glob matches nothing until an app deploys.

#### 3.3.4 How apps attach

Each app's deploy writes a self-contained site block to
`/srv/caddy/sites/<app>.caddy` (and `chcon`s it for SELinux), then reloads Caddy:

```bash
podman exec caddy caddy reload --config /etc/caddy/Caddyfile
```

demopage's CI does exactly this with `demopage.caddy`, pointing
`api.kalandra.tech` at the active blue/green port. A second app (hampap) adds
its own `hampap.caddy` for its own hostname and cert — the two coexist, and a
reload triggered by one app re-reads the whole imported config without dropping
the other's traffic.

#### 3.3.5 Configure Cloudflare SSL Mode

In Cloudflare dashboard → **SSL/TLS → Overview**, set the mode to **Full (strict)** (account-wide). This makes Cloudflare validate the origin certificate when connecting to your VM.

### 3.4 Enable IPv6 on the VCN

Oracle Cloud VCNs are IPv4-only by default. IPv6 is required for the backend to reach Supabase PostgreSQL (which resolves to an IPv6 address). All steps are in the OCI Console.

#### 3.4.1 Add IPv6 to VCN

1. **Networking → Virtual Cloud Networks** → click your VCN
2. Click **Add IPv6 CIDR Block/Prefix**
3. Choose **Oracle-allocated IPv6 /56 prefix**
4. Click **Add**

#### 3.4.2 Add IPv6 to Subnet

1. Inside the VCN, go to **Subnets** → click your subnet
2. Click **Add IPv6 CIDR Block/Prefix**
3. Choose a `/64` from the VCN's `/56` allocation
4. Click **Add**

#### 3.4.3 Add IPv6 Route

1. Inside the VCN, go to **Route Tables** → click the subnet's route table
2. **Add Route Rule**:
   - Destination: `::/0`
   - Target Type: Internet Gateway
   - Target: your existing Internet Gateway
3. Click **Add**

#### 3.4.4 Add IPv6 Security Rules

In **Security Lists** (or your Network Security Group), add:

**Egress** (required — outbound to Supabase):
- Stateful: Yes
- Destination: `::/0`
- Protocol: TCP
- Destination Port Range: All (or 5432, 443 for minimal access)

**Ingress** (optional — if you want the API reachable over IPv6):
- Source: `::/0`
- Protocol: TCP
- Destination Port Range: 80, 443, 8080

#### 3.4.5 Assign IPv6 Address to the VM

1. **Compute → Instances** → click your instance
2. Under **Resources → Attached VNICs** → click the VNIC
3. Under **Resources → IPv6 Addresses** → click **Assign IPv6 Address**
4. Choose **Automatically assign from subnet prefix**
5. Click **Assign**

#### 3.4.6 Verify IPv6 on the VM

SSH into the VM (`ssh opc@<public-ip>`) and verify:

```bash
# Verify IPv6 is not disabled (should return 0)
sysctl net.ipv6.conf.all.disable_ipv6

# Confirm a GUA (2603:...) address is assigned
ip -6 addr show

# Test outbound IPv6 (TCP — ICMP ping may be blocked by OCI)
curl -6 -v --connect-timeout 5 https://ipv6.google.com 2>&1 | head -5
```

Oracle Linux uses `firewalld`. If ICMPv6 is needed for debugging:

```bash
sudo firewall-cmd --add-protocol=ipv6-icmp --permanent
sudo firewall-cmd --reload
```

> **Note:** No podman IPv6 configuration is needed — the backend container uses host networking (`Network=host`), so it shares the host's IPv6 stack directly.

### 3.5 DNS

In Cloudflare DNS, add an A record for `api.kalandra.tech` pointing to your OCI VM's public IP. Keep it **Proxied** (orange cloud) — Cloudflare handles public TLS, Caddy uses the origin certificate for the Cloudflare-to-origin connection.

---

## 4. CI/CD Configuration

The deploy assumes the shared host infrastructure from §3.2–3.3 is already in
place — podman installed, linger enabled, the shared Caddy running, and
`/srv/caddy/sites` present. It checks these at the top of the run and aborts
with a pointer to this doc if any is missing; it does **not** install or repair
them.

### 4.1 GitHub Repository Secrets

Add these secrets in **Settings → Secrets and Variables → Actions**:

| Secret | Value |
|--------|-------|
| `OCI_HOST` | Your OCI VM public IP |
| `OCI_USERNAME` | SSH username (`opc` for Oracle Linux) |
| `OCI_SSH_KEY` | Private SSH key for the VM |
| `DB_CONNECTION_STRING` | `Host=db.<project-ref>.supabase.co;Database=postgres;Username=postgres;Password=<DB_PASSWORD>;Port=5432` |
| `SUPABASE_PROJECT_URL` | `https://your-project.supabase.co` |
| `SUPABASE_SERVICE_ROLE_KEY` | Service role key from Supabase dashboard (**Settings → API**) — used by the backend for storage uploads |
| `SUPABASE_PUBLISHABLE_KEY` | Publishable key from Supabase dashboard (mapped to `PUBLIC_SUPABASE_PUBLISHABLE_KEY` at frontend build time) |
| `TURNSTILE_SECRET_KEY` | Cloudflare Turnstile secret key (from [Turnstile dashboard](https://dash.cloudflare.com/?to=/:account/turnstile)) — used by backend to verify CAPTCHA tokens |
| `TURNSTILE_SITE_KEY` | Cloudflare Turnstile site key (public, mapped to `PUBLIC_TURNSTILE_SITE_KEY` at frontend build time) |
| `BACKEND_SENTRY_DSN` | DSN from the Sentry **backend (.NET)** project — written to `Sentry__Dsn` at deploy time. **Required in production**; the API throws on startup if it's missing. |
| `SENTRY_CI_TOKEN` | Sentry **organization auth token** (scope `org:ci`) used by `@sentry/vite-plugin` to upload frontend source maps during `frontend-deploy`. Create at **Settings → Auth Tokens** (org level). Mapped to `SENTRY_AUTH_TOKEN` at build time. Omitting it silently skips the upload — the deploy still succeeds, just without resolved stack traces. |

Plus these repository **variables** (Settings → Variables → Actions) used by the same source-map upload step:

| Variable | Value |
|----------|-------|
| `SENTRY_ORG` | Sentry organisation slug (visible in the URL — `https://<org>.sentry.io`). Optional with an org auth token, which already carries the org. |
| `SENTRY_PROJECT` | Slug of the **frontend (Browser JavaScript)** project |

The org lives in Sentry's EU region (the DSN host is `ingest.de.sentry.io`), but no `SENTRY_URL` is
set — the org auth token embeds its own region endpoint and the upload routes there automatically.

The **frontend** Sentry DSN is committed in `frontend/.env` — browser DSNs are public credentials
(protected by the per-project Allowed Domains list in Sentry, not by secrecy) so a GitHub secret
adds no value.

### 4.2 GitHub Actions Environment

Create a `production` environment in **Settings → Environments**:
- Add protection rules (optional): require approval for deployments

### 4.3 Container Registry Auth

The CI/CD uses GitHub Container Registry (GHCR). The `GITHUB_TOKEN` is
automatic for the build/push step. The OCI VM also pulls from GHCR — see
[Authenticate to GHCR](#authenticate-to-ghcr) under §3.2 for the manual
`podman login` step.

---

## 5. Observability

Errors, structured logs, traces, and session replays are sent to
[Sentry](https://sentry.io). The backend uses `Sentry.AspNetCore` +
`Sentry.OpenTelemetry` to bridge its existing OTEL pipeline; the frontend uses
`@sentry/browser` (npm SDK), dynamic-imported from
[`src/lib/observability.ts`](../frontend/src/lib/observability.ts) so the
chunk tree-shakes out when no DSN is configured.

### 5.1 Sentry Projects

Create **two separate projects** in your Sentry organisation — the platforms
differ, so the DSNs and source-map / release tooling won't be interchangeable:

| Project | Platform | DSN goes to |
|---------|----------|-------------|
| Backend API | .NET → ASP.NET Core | GitHub secret `BACKEND_SENTRY_DSN` |
| Frontend site | JavaScript → Browser | `frontend/.env` (committed, public credential) |

For each project:

1. Sentry dashboard → **Projects → Create Project** → pick the platform above.
2. Copy the **DSN** from the project's **Settings → Client Keys (DSN)** page.
3. On the same page, set **Allowed Domains** to `https://www.kalandra.tech`,
   `http://localhost:*`, `http://127.0.0.1:*`. This is what protects the
   public frontend DSN from being abused by anyone who scrapes it from the bundle.
4. Paste the backend DSN into the matching GitHub repository secret (§4.1);
   paste the frontend DSN into `frontend/.env`.

Sentry's free tier is sufficient for low-traffic personal projects; bump it
later if you outgrow the event quota.

### 5.2 Environments and CI noise

The frontend tags Sentry events with an `environment` derived from Vite's
build mode by default (`development` in `astro dev`, `production` in
`astro build`). CI Playwright jobs build with `PUBLIC_SENTRY_DSN=""` in
[.github/workflows/ci-cd.yml](../.github/workflows/ci-cd.yml), which makes
Vite tree-shake the `@sentry/browser` chunk out of the bundle at build
time — so CI emits zero Sentry events and prod dashboards stay clean
without needing an `!environment:ci` filter.

### 5.3 Local Development

You don't need a Sentry DSN to run the stack locally — but the frontend DSN
is committed, so by default any `npm run aspire` session will emit
`environment: development` events to Sentry. That's intentional (lets you
verify changes against real Sentry). To opt out locally, set
`PUBLIC_SENTRY_DSN=` in `frontend/.env.local`.

The backend, by contrast, only **requires** a DSN when
`ASPNETCORE_ENVIRONMENT=Production`. In `Development` (the AppHost default)
it's optional; a missing configuration won't break local runs. To exercise
the production path locally:

```bash
dotnet user-secrets --project backend/src/Kalandra.Api set "Sentry:Dsn" "<your-dsn>"
```

### 5.4 Source Maps

The `frontend-deploy` job runs `@sentry/vite-plugin` after `astro build`
to upload the generated `.map` files to Sentry and tag the upload with
the deploy commit (`GITHUB_SHA`). Without this, stack traces in Sentry
point at minified chunk names like `_astro/Layout…QS3rhwpU.js:1:42`
instead of the original `.astro` / `.ts` source line.

Inputs (see §4.1):

- `SENTRY_CI_TOKEN` (secret, mapped to `SENTRY_AUTH_TOKEN` at build time) — required for upload. Missing → upload is skipped silently.
- `SENTRY_PROJECT` (and optionally `SENTRY_ORG`) repo vars — point the upload at the right Sentry project.

`astro.config.mjs` sets `build.sourcemap: 'hidden'` only when the auth
token is present, so dev (`npm run aspire`) and CI builds don't generate
`.map` files at all.
