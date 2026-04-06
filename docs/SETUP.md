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
  - [3.3 Reverse Proxy (Caddy)](#33-reverse-proxy-caddy)
  - [3.4 Enable IPv6 on the VCN](#34-enable-ipv6-on-the-vcn)
  - [3.5 DNS](#35-dns)
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
PUBLIC_TURNSTILE_SITE_KEY=your-real-site-key
```

#### Cloudflare Turnstile (CAPTCHA)

The committed `.env` uses Cloudflare's [always-pass test keys](https://developers.cloudflare.com/turnstile/troubleshooting/testing/) so the form works locally without a real Turnstile widget. The backend `appsettings.json` uses the matching always-pass test secret (`1x0000000000000000000000000000000AA`).

To test with a real widget locally, override in `.env.local` (frontend) and `appsettings.Development.json` or user-secrets (backend):
```
# frontend/.env.local
PUBLIC_TURNSTILE_SITE_KEY=your-real-site-key

# backend — via environment variable or appsettings override
Turnstile__SecretKey=your-real-secret-key
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
   - Image: Oracle Linux 8 aarch64 (ARM image for A1 shape)
   - Add your SSH public key
3. Note the **public IP address**

#### Configure VM

SSH into the instance and install Docker:

```bash
# Oracle Linux 8
sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Log out and back in for group changes
exit
```

#### Configure Firewall

```bash
# Open ports 80 (HTTP), 443 (HTTPS), 8080 (API)
sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
sudo iptables -I INPUT -p tcp --dport 8080 -j ACCEPT
sudo netfilter-persistent save
```

Also add ingress rules in OCI Console:
- **Networking → Virtual Cloud Networks → Security Lists**
- Add ingress rules for ports 80, 443, 8080

### 3.3 Reverse Proxy (Caddy)

Caddy provides automatic HTTPS:

```bash
# Create Caddyfile
cat > ~/Caddyfile << 'EOF'
api.kalandra.tech {
    reverse_proxy localhost:8080
}
EOF

# Run Caddy
docker run -d \
  --name caddy \
  --restart unless-stopped \
  --network host \
  -v ~/Caddyfile:/etc/caddy/Caddyfile \
  -v caddy_data:/data \
  -v caddy_config:/config \
  caddy:2-alpine
```

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

> **Note:** No Docker IPv6 configuration is needed — the backend container runs with `--network host`, so it shares the host's IPv6 stack directly.

### 3.5 DNS

Add an A record for `api.kalandra.tech` pointing to your OCI VM's public IP.

---

## 4. CI/CD Configuration

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

### 4.2 GitHub Actions Environment

Create a `production` environment in **Settings → Environments**:
- Add protection rules (optional): require approval for deployments

### 4.3 Container Registry Auth

The CI/CD uses GitHub Container Registry (GHCR). The `GITHUB_TOKEN` is automatic.

On the OCI VM, authenticate to GHCR:
```bash
echo <GITHUB_PAT> | docker login ghcr.io -u <GITHUB_USERNAME> --password-stdin
```

Create a GitHub Personal Access Token with `read:packages` scope.
