# Setup Guide — kalandra.tech

Step-by-step guide for local development, testing, and deployment infrastructure.

For architecture, tech stack, and decision log, see [PROJECT.md](PROJECT.md).

---

## Table of Contents

- [1. Local Development](#1-local-development)
  - [1.1 Prerequisites](#11-prerequisites)
  - [1.2 Install Dependencies](#12-install-dependencies)
  - [1.3 JetBrains Run Configurations (recommended)](#13-jetbrains-run-configurations-recommended)
  - [1.4 CLI Alternative](#14-cli-alternative)
  - [1.5 Local Supabase](#15-local-supabase)
  - [1.6 Frontend Environment](#16-frontend-environment)
  - [1.7 Stopping Services](#17-stopping-services)
- [2. Running Tests](#2-running-tests)
  - [2.1 Backend Integration Tests](#21-backend-integration-tests)
  - [2.2 Frontend Page Tests](#22-frontend-page-tests)
  - [2.3 E2E Tests](#23-e2e-tests)
- [3. Infrastructure Setup](#3-infrastructure-setup)
  - [3.1 Supabase Project](#31-supabase-project)
  - [3.2 Oracle Cloud VM](#32-oracle-cloud-vm)
  - [3.3 Container Setup (Quadlet + systemd)](#33-container-setup-quadlet--systemd)
  - [3.4 DNS](#34-dns)
- [4. CI/CD Configuration](#4-cicd-configuration)
  - [4.1 GitHub Repository Secrets](#41-github-repository-secrets)
  - [4.2 GitHub Actions Environment](#42-github-actions-environment)
  - [4.3 Container Registry Auth](#43-container-registry-auth)

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

### 1.3 JetBrains Run Configurations (recommended)

The `.run/` directory contains shared run configurations that handle dependency installation and infrastructure startup automatically:

- **Debug Backend** — launches the .NET backend with debugger attached + Astro dev server. Infrastructure (PostgreSQL + local Supabase) starts automatically as a before-launch step. Use this when you need to set breakpoints in the backend.
- **Watch BE+FE** — launches `dotnet watch` + `astro dev` side-by-side with separate log panels. Both backend and frontend hot-reload on file changes. Use this for everyday development.

Both configurations run `npm install` as a before-launch step, so dependencies are always up to date.

> The remaining configurations in `.run/` (`Backend`, `Frontend`, `Watch Backend`) are building blocks used by the compound configs above.

### 1.4 CLI Alternative

```bash
# From the repo root — starts PostgreSQL, local Supabase, backend (dotnet watch), and frontend (astro dev)
npm run dev
```

This starts:
- **PostgreSQL** (port 5432) — backend event store
- **Local Supabase** (port 54321) — auth, API gateway, Studio dashboard
- **Backend** (port 5000) — .NET API with hot reload
- **Frontend** (port 4321) — Astro dev server

### 1.5 Local Supabase

The project includes a `supabase/config.toml` that configures a local Supabase instance with email/password auth (no email confirmation required). On first run, `supabase start` downloads the required Docker images (~2-3 min).

Local services:
| Service | URL |
|---------|-----|
| API gateway | `http://localhost:54321` |
| Studio dashboard | `http://localhost:54323` |
| Inbucket (email) | `http://localhost:54324` |

Local credentials (well-known dev values, not secrets):
- **Publishable key**: `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0`

### 1.6 Frontend Environment

The committed `frontend/.env` has local Supabase defaults — ready to use out of the box.

To point at a different Supabase instance, create `frontend/.env.local` (gitignored) to override:
```
PUBLIC_SUPABASE_URL=https://your-project.supabase.co
PUBLIC_SUPABASE_PUBLISHABLE_KEY=your-publishable-key
PUBLIC_API_URL=http://localhost:5000
```

### 1.7 Stopping Services

```bash
npm run dev:stop     # Stop PostgreSQL + local Supabase
npm run dev:wipe     # Stop and delete all data (clean slate)
```

---

## 2. Running Tests

```bash
npm test               # Runs all tests: backend + frontend + E2E
```

### 2.1 Backend Integration Tests

Requires Docker (Testcontainers spins up a real PostgreSQL container):

```bash
npm run test:backend
```

### 2.2 Frontend Page Tests

Builds the static site, serves it, and verifies page rendering, navigation, i18n, and dark mode:

```bash
npm run test:frontend  # Installs Playwright browsers automatically
```

### 2.3 E2E Tests

Runs Playwright against the full stack (frontend + backend + DB):

```bash
npm run test:e2e
```

---

## 3. Infrastructure Setup

This section covers the one-time setup needed to host this project yourself.

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

#### Create Always Free VM

1. Sign up for [Oracle Cloud Free Tier](https://cloud.oracle.com/free)
2. Create a Compute instance:
   - Shape: `VM.Standard.A1.Flex` (ARM, 4 OCPU / 24 GB RAM — Always Free)
   - Image: Oracle Linux 9 aarch64 (ships with rootless `podman`)
   - Add your SSH public key
3. Note the **public IP address**

> The instructions below assume Oracle Linux 9 + rootless podman running under
> the default `opc` user. If you use Ubuntu, install `podman` from apt and
> adjust the username accordingly.

#### Install podman and enable linger

```bash
ssh opc@<your-vm-ip>

sudo dnf install -y podman
podman --version    # should be 4.4+ for Quadlet support

# Enable linger so the user systemd instance keeps running after logout.
# Without this, every container dies the moment the SSH session ends.
sudo loginctl enable-linger opc
```

#### Configure firewall

```bash
# Open ports 80 (HTTP), 443 (HTTPS) for Caddy
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload
```

Also add ingress rules in OCI Console:
- **Networking → Virtual Cloud Networks → Security Lists**
- Add ingress rules for ports 80 and 443

### 3.3 Container Setup (Quadlet + systemd)

The API runs in two slots — `kalandra-api-blue` (port 8080) and
`kalandra-api-green` (port 8081). At any time at most one slot is enabled
and running; the deploy pipeline swaps slots on each release. Caddy fronts
whichever port is currently active.

All three containers (blue, green, caddy) are managed by systemd via Quadlet
unit files. The unit files live in [`infra/quadlet/`](../infra/quadlet) in
the repo and are installed manually on the VM (one-time setup).

#### Authenticate to GHCR

The blue/green images are pulled from GitHub Container Registry. Create a
GitHub Personal Access Token with `read:packages` scope and log in:

```bash
echo <GITHUB_PAT> | podman login ghcr.io -u <GITHUB_USERNAME> --password-stdin
```

#### Install the Quadlet unit files

Copy the four Quadlet files from the repo to the user's systemd directory:

```bash
mkdir -p ~/.config/containers/systemd

# Copy from a local checkout (or scp/curl the raw files from GitHub)
cp infra/quadlet/kalandra-api-blue.container  ~/.config/containers/systemd/
cp infra/quadlet/kalandra-api-green.container ~/.config/containers/systemd/
cp infra/quadlet/caddy.container              ~/.config/containers/systemd/
cp infra/quadlet/caddy_data.volume            ~/.config/containers/systemd/
cp infra/quadlet/caddy_config.volume          ~/.config/containers/systemd/
```

#### Create the secrets env file

The API units load environment variables (DB connection, Supabase URL, etc.)
from `~/kalandra-api.env`. The CI/CD deploy script rewrites this file on
every run, but it must exist before the unit can start the first time:

```bash
umask 077
cat > ~/kalandra-api.env << 'EOF'
ConnectionStrings__DefaultConnection=Host=db.<project-ref>.supabase.co;Database=postgres;Username=postgres;Password=<DB_PASSWORD>;Port=5432
Auth__SupabaseProjectUrl=https://<project-ref>.supabase.co
Storage__SupabaseProjectUrl=https://<project-ref>.supabase.co
Storage__ServiceKey=<service-role-key>
EOF
```

#### Create the initial Caddyfile

Caddy reads `~/Caddyfile`, which the deploy script rewrites on each slot
swap. Seed it with the blue port (8080) for first boot:

```bash
cat > ~/Caddyfile << 'EOF'
api.kalandra.tech {
    reverse_proxy localhost:8080
}
EOF
```

#### Enable and start the services

```bash
systemctl --user daemon-reload

# Start Caddy (TLS/proxy)
systemctl --user enable --now caddy.service

# Start ONE API slot. We pick blue arbitrarily — the deploy pipeline
# will swap to green on the next release. Do not enable both: only one
# slot should run at a time so background jobs are not double-processed.
systemctl --user enable --now kalandra-api-blue.service

# Verify
systemctl --user status kalandra-api-blue.service caddy.service
curl http://localhost:8080/health
curl https://api.kalandra.tech/health
```

#### Useful commands

```bash
# Watch logs in real time
journalctl --user -u kalandra-api-blue.service -f
journalctl --user -u caddy.service -f

# Manually swap slots (the deploy pipeline does this automatically)
systemctl --user enable --now  kalandra-api-green.service
systemctl --user disable --now kalandra-api-blue.service

# Force a fresh image pull and restart (also done automatically on deploy)
podman pull ghcr.io/<owner>/<repo>/api:latest
systemctl --user restart kalandra-api-blue.service
```

### 3.4 DNS

Add an A record for `api.kalandra.tech` pointing to your OCI VM's public IP.

---

## 4. CI/CD Configuration

### 4.1 GitHub Repository Secrets

Add these secrets in **Settings → Secrets and Variables → Actions**:

| Secret | Value |
|--------|-------|
| `OCI_HOST` | Your OCI VM public IP |
| `OCI_USERNAME` | SSH username (e.g., `opc` for Oracle Linux) |
| `OCI_SSH_KEY` | Private SSH key for the VM |
| `DB_CONNECTION_STRING` | `Host=db.<project-ref>.supabase.co;Database=postgres;Username=postgres;Password=<DB_PASSWORD>;Port=5432` |
| `SUPABASE_PROJECT_URL` | `https://your-project.supabase.co` |
| `SUPABASE_SERVICE_ROLE_KEY` | Service role key from Supabase dashboard (**Settings → API**) — used by the backend for storage uploads |
| `SUPABASE_PUBLISHABLE_KEY` | Publishable key from Supabase dashboard (mapped to `PUBLIC_SUPABASE_PUBLISHABLE_KEY` at frontend build time) |

### 4.2 GitHub Actions Environment

Create a `production` environment in **Settings → Environments**:
- Add protection rules (optional): require approval for deployments

### 4.3 Container Registry Auth

The CI/CD uses GitHub Container Registry (GHCR). The `GITHUB_TOKEN` is
automatic for the build/push step. The OCI VM also pulls from GHCR — see
[3.3 Container Setup](#33-container-setup-quadlet--systemd) for the manual
`podman login` step.
