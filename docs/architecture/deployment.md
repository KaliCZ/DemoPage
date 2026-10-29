# Deployment

## Purpose

Keep the site up through deploys and reboots at close to zero cost, with every release automated
([technical requirements](README.md#technical-requirements)).

## How it works

- **Frontend** — GitHub Actions builds the static site and deploys it to the Cloudflare Pages
  project `kalandra-tech`.
- **Backend** — the API and the MCP host ship as container images to GHCR and run on one Oracle
  Cloud VM as rootless podman containers managed by systemd Quadlet units, each with a blue and a
  green slot. Only one slot per host is live; Caddy routes its hostname to it.
- **Pipeline** (`.github/workflows/ci-cd.yml`) — every PR and every push to `main` runs the backend
  tests, the frontend page tests, a Prettier check, and the full-stack E2E suite. On `main`, once
  all pass, `backend-deploy` releases the API, then `mcp-deploy` releases the MCP host (the API
  applies the schema first), and `frontend-deploy` publishes the site. A manual run can skip the
  backend deploy to ship the frontend while the backend is broken.
- **Blue/green release** — a deploy writes the host's env file, installs the idle slot's unit with
  `Image=` pinned to the digest this run built, starts it, and waits until `/health/live` reports
  the expected commit. Only then does it point the host's Caddy fragment at the new port, reload
  Caddy, and remove the old slot's unit. `/health` (which also checks the database and Supabase)
  only warns, so an outage of a dependency can't block a fix from shipping.
- **Shared host** — the VM is multi-tenant: other apps (hampap) share its rootless podman under
  `opc` and its Caddy. This repo's deploys install only their own containers and Caddy fragments;
  they never install podman or manage Caddy, and fail fast if the shared pieces are missing.
- **Reboot survival** — linger keeps `opc`'s `systemd --user` running without a login; Quadlet's
  `WantedBy=default.target` starts Caddy and each live slot on boot; and the env files, the pinned
  unit files, the Caddy fragments, and the certs are all on disk. A cold boot brings back exactly
  the live slots, routed as before. A reboot during a deploy, before the swap, brings back the old
  slot; the next deploy recreates the other one.

### Ports and files on the VM

Every app runs with `Network=host` and shares the host's ports. Only Caddy is public; the `80xx`
ports are localhost wiring behind it, so the only rule is not to collide.

| Port(s)     | Owner                       | Notes                                              |
|-------------|-----------------------------|----------------------------------------------------|
| 80, 443     | Caddy (shared)              | The only public ports; routes to apps by hostname. |
| 8080 / 8081 | kalandra API                | Blue / green slots.                                |
| 8082 / 8083 | kalandra MCP host           | Blue / green slots.                                |
| 3001, 6379, 13000–13001, 18080–18090 | hampap (neighbor app) | Listed so this repo's blocks stay clear of it. The hampap repo's `docs/infrastructure.md` port map is its source of truth and records these ports in return. |

A new app takes the next free block (for example `81xx`), checked against hampap's map as well,
and gets a row here before its Caddy fragment is wired up.

This repo's deploys write to `~/demoPage/`: the slots' env files (`kalandra-api.env`,
`kalandra-mcp.env`), the Caddy routing templates, and a staging folder for unit files. Installed
units live in `~/.config/containers/systemd/`, and the Caddy fragments and certs in `/srv/caddy/`,
all named `kalandra*` so they never collide with another app's.

## Decisions

- **Oracle Cloud Always Free (ARM A1)** — a free, always-on VM with generous specs that can run
  long-lived processes and containers. Hetzner (about €4/month) is the fallback if Oracle becomes
  unreliable. Not serverless or scale-to-zero platforms (Lambda, Cloud Run, Render's free tier,
  Azure App Service F1), which stop the process the background daemon needs.
- **Rootless podman with Quadlet** — Oracle Linux ships podman, and Quadlet units get systemd's
  restart and boot handling with no extra orchestrator.
- **Cloudflare in front, Caddy at the origin** — Cloudflare terminates public TLS and proxies every
  hostname; Caddy presents a Cloudflare Origin Certificate and Cloudflare runs in Full (strict)
  mode, so origin traffic is encrypted and verified without Let's Encrypt.
- **One Caddy fragment per host** — the API and MCP deploys each rewrite only their own file
  (`kalandra.caddy`, `kalandra-mcp.caddy`), and the base Caddyfile imports every fragment.
- **Images pinned by digest** — a restart or reboot can never drift onto a different build than the
  one the pipeline verified.

## Invariants

- Only ports 80 and 443 are open to the internet; the slot ports are reachable only through Caddy.
- A deploy promotes a slot only after it reports the commit being deployed.
- `mcp-deploy` runs after `backend-deploy`, because the API owns the schema.
- Production configuration comes only from GitHub secrets and variables; both hosts refuse to
  start in production when a database, Supabase, mail, feed, or public URL still points at
  localhost, or when the Sentry DSN is missing.

## Setting it up from scratch

One-time steps for a new environment. The VM and Caddy are shared host infrastructure; set them
up once per machine.

### Supabase project

1. Create a project at [supabase.com](https://supabase.com). Note the **Project URL** and the
   **publishable key** (Settings → API). The backend fetches the JWT signing keys through JWKS.
2. **Google sign-in** — Authentication → Providers → Google. Create a Web application OAuth client
   in the [Google Cloud Console](https://console.cloud.google.com/apis/credentials) with the
   redirect URI `https://<project-ref>.supabase.co/auth/v1/callback`, and paste its client id and
   secret into Supabase.
3. **Redirect URLs** — Authentication → URL Configuration: Site URL `https://www.kalandra.tech`;
   redirect URLs `https://www.kalandra.tech/**`, `https://kalandra.tech/**`, and
   `http://localhost:4321/**`.
4. **CAPTCHA** — Authentication → Attack Protection: enable Cloudflare Turnstile with the
   production secret, so sign-in, sign-up, and password reset require a token.
5. **Auth emails** — Supabase's built-in mailer only delivers to project members, so set a custom
   SMTP server under Authentication → Emails → SMTP Settings. With Brevo, the password is an
   **SMTP key**; REST API keys can't authenticate SMTP. These credentials are separate from the
   backend's `SMTP_PASSWORD`.
6. **Storage** — Storage → New bucket `job-offer-attachments`: private, 15 MB file limit, and the
   MIME types listed for it in `supabase/config.toml` (locally, `supabase start` creates it from
   that file).
7. **Admin user** — after signing in once, grant the role in the SQL editor, with the admin's own
   address in the `WHERE` clause:

   ```sql
   UPDATE auth.users
   SET raw_app_meta_data = raw_app_meta_data || '{"roles": ["admin"]}'::jsonb
   WHERE email = 'your@email.com';
   ```

If `/auth/v1/recover` returns a 5xx: find the request in **Logs → Auth** by its timestamp and
`sb-request-id`; the logged error tells SMTP failures apart from database timeouts and failed
CAPTCHA checks. Check the [Brevo SMTP settings](https://developers.brevo.com/docs/smtp-integration)
for SMTP errors and Supabase's resource metrics for timeouts, then verify by requesting a reset
link and signing in with the new password.

### Oracle Cloud VM

1. Create a compute instance: shape `VM.Standard.A1.Flex` (Always Free up to 4 OCPU / 24 GB RAM),
   image **Oracle Linux 9** (aarch64), your SSH public key. The login user is `opc`.
2. Make sure podman is present: `sudo dnf install -y podman`. SELinux stays enforcing; the Quadlet
   `Volume=` lines relabel with `:z`, and the deploy `chcon`s each Caddy fragment it writes.
3. Log in to GHCR once as `opc` with a GitHub personal access token that has `read:packages`; the
   credentials persist in `~/.config/containers/auth.json`:

   ```bash
   read -rsp "GitHub PAT (read:packages): " GHCR_TOKEN && echo "$GHCR_TOKEN" | podman login ghcr.io -u KaliCZ --password-stdin
   ```

4. Open the public ports in the OS firewall and in the VCN's security list (Networking → Virtual
   Cloud Networks → Security Lists → ingress for 80 and 443):

   ```bash
   sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
   sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
   sudo netfilter-persistent save
   ```

5. Optional host operations, documented next to their unit files: automatic security updates and
   the reboot notifier ([`infra/host/os-updates.md`](../../infra/host/os-updates.md)), and the
   disk-space and daily health alerts ([`infra/host/monitoring.md`](../../infra/host/monitoring.md)).

The API and MCP slots need no manual setup — the deploy installs and manages their units from
[`infra/quadlet/`](../../infra/quadlet/).

### Shared Caddy reverse proxy

Caddy runs rootless under `opc` as a Quadlet unit and reads its config from `/srv/caddy`:
`Caddyfile` ([`infra/host/Caddyfile`](../../infra/host/Caddyfile), which imports
`/etc/caddy/sites/*.caddy`), `sites/` (one fragment per hostname, written by each app's deploy),
and `certs/`.

```bash
sudo install -d -o "$USER" -g "$USER" /srv/caddy /srv/caddy/sites /srv/caddy/certs
cp infra/host/Caddyfile /srv/caddy/Caddyfile

mkdir -p ~/.config/containers/systemd
cp infra/host/caddy.container infra/host/caddy-data.volume infra/host/caddy-config.volume ~/.config/containers/systemd/

# Let the rootless container bind 80/443, and keep user services alive across reboots
echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-unprivileged-ports.conf
sudo sysctl --system
sudo loginctl enable-linger "$USER"
```

Without a checkout on the VM, paste the files from [`infra/host/`](../../infra/host/) instead.

**Certificate** — Cloudflare dashboard → SSL/TLS → Origin Server → Create Certificate for
`*.kalandra.tech` and `kalandra.tech` (15 years, PEM). Both kalandra fragments expect it as
`kalandra.pem` / `kalandra.key`:

```bash
nano /srv/caddy/certs/kalandra.pem
nano /srv/caddy/certs/kalandra.key
chmod 600 /srv/caddy/certs/kalandra.key
```

Then start Caddy. An empty `sites/` folder is fine — the import matches nothing until an app
deploys.

```bash
systemctl --user daemon-reload
systemctl --user start caddy.service
```

An app attaches by writing `/srv/caddy/sites/<name>.caddy` and running
`podman exec caddy caddy reload --config /etc/caddy/Caddyfile`; a reload re-reads every fragment
without dropping another app's traffic.

Finally, set Cloudflare → SSL/TLS → Overview to **Full (strict)**.

### IPv6

The backend reaches Supabase's PostgreSQL over IPv6, and OCI VCNs are IPv4-only by default. In the
OCI Console:

1. **VCN** — Add IPv6 CIDR Block/Prefix → Oracle-allocated `/56`.
2. **Subnet** — Add IPv6 CIDR Block/Prefix → a `/64` from the VCN's `/56`.
3. **Route table** — a rule for `::/0` to the existing Internet Gateway.
4. **Security list** — a stateful egress rule to `::/0` over TCP (all ports, or 5432 and 443);
   optionally ingress from `::/0` on 80 and 443.
5. **VNIC** — Compute → Instances → the instance → Attached VNICs → IPv6 Addresses → Assign,
   automatically from the subnet prefix.

Verify on the VM — the containers use host networking, so podman needs no IPv6 configuration:

```bash
sysctl net.ipv6.conf.all.disable_ipv6
ip -6 addr show
curl -6 -v --connect-timeout 5 https://ipv6.google.com 2>&1 | head -5
```

### DNS

In Cloudflare DNS, add **proxied** A records for `api.kalandra.tech` and `mcp.kalandra.tech` to
the VM's public IP; the site itself is a Cloudflare Pages custom domain.

### GitHub configuration

Create a `production` environment (Settings → Environments) for the deploy jobs. The workflow is
the authoritative list of what it reads; this is where each value comes from.

**Secrets** (Settings → Secrets and variables → Actions):

| Secret | Value |
|--------|-------|
| `OCI_SSH_KEY` | Private SSH key for `opc` on the VM |
| `DB_CONNECTION_STRING` | `Host=db.<project-ref>.supabase.co;Database=postgres;Username=postgres;Password=…;Port=5432` |
| `SUPABASE_SERVICE_ROLE_KEY` | Supabase → Settings → API; the backend uses it for storage and the admin API |
| `TURNSTILE_SECRET_KEY` | [Turnstile dashboard](https://dash.cloudflare.com/?to=/:account/turnstile) |
| `BACKEND_SENTRY_DSN` | DSN of the Sentry backend project ([observability.md](observability.md)) |
| `SENTRY_CI_TOKEN` | Sentry organization auth token (scope `org:ci`) for source-map uploads |
| `SMTP_USERNAME` / `SMTP_PASSWORD` | Login for the SMTP relay that sends notifications |
| `CLOUDFLARE_API_TOKEN` | Cloudflare API token allowed to deploy Pages |

**Variables:**

| Variable | Value |
|----------|-------|
| `OCI_HOST` / `OCI_USERNAME` | The VM's public IP and `opc` |
| `SUPABASE_PROJECT_URL` | `https://<project-ref>.supabase.co` |
| `SUPABASE_PUBLISHABLE_KEY` | Supabase publishable key, baked into the frontend build |
| `TURNSTILE_SITE_KEY` | Turnstile site key, baked into the frontend build |
| `CLOUDFLARE_ACCOUNT_ID` | The Cloudflare account that owns the Pages project |
| `SENTRY_ORG` / `SENTRY_PROJECT` | Organization slug (optional with an org token) and the frontend project slug |
| `SMTP_HOST` / `SMTP_PORT` / `SMTP_FROM_EMAIL` | The relay and the From address of notification mail |
| `BLOG_AUTHOR_NOTIFICATION_EMAIL` | Mailbox that receives blog comment notifications |
| `JOB_OFFERS_OWNER_NOTIFICATION_EMAIL` | Mailbox that receives job-offer notifications |

The pipeline pushes images to GHCR with the automatic `GITHUB_TOKEN`; the VM pulls with the
token from its one-time `podman login`.
