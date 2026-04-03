# Setup Guide — kalandra.tech v2–v4

Step-by-step guide for setting up the backend, auth, and deployment infrastructure.

---

## 1. Supabase Project Setup

### 1.1 Create Supabase Project

1. Go to [supabase.com](https://supabase.com) and create a new project
2. Note these values from **Settings → API**:
   - **Project URL** (e.g., `https://abcdef.supabase.co`)
   - **Publishable key** (safe for browser)
   - **JWT keys** — the backend fetches these automatically via JWKS; no manual configuration needed

### 1.2 Configure OAuth Provider (Google)

1. In Supabase dashboard: **Authentication → Providers → Google**
2. Enable Google provider
3. Create OAuth credentials in [Google Cloud Console](https://console.cloud.google.com/apis/credentials):
   - Application type: Web application
   - Authorized redirect URI: `https://<your-project-ref>.supabase.co/auth/v1/callback`
4. Paste the Client ID and Client Secret into Supabase
5. Optionally enable GitHub as a second provider (same flow)

### 1.3 Configure Redirect URLs

In **Authentication → URL Configuration**:
- **Site URL**: `https://www.kalandra.tech`
- **Redirect URLs** (add all):
  - `https://www.kalandra.tech/**`
  - `https://kalandra.tech/**`
  - `http://localhost:4321/**` (for local development)

### 1.4 Set Up Admin User

The backend uses role-based authorization. Roles are stored as an array in the user's Supabase `app_metadata`, included in the JWT, and mapped to standard .NET role claims.

After signing in for the first time, assign the admin role:

```sql
-- Run in Supabase SQL Editor to set admin via app_metadata
UPDATE auth.users
SET raw_app_meta_data = raw_app_meta_data || '{"roles": ["admin"]}'::jsonb
WHERE email = 'your@email.com';
```

This user will have admin privileges (can see all submissions, update statuses). The backend reads `app_metadata.roles` from the JWT and maps each entry to a .NET role claim. The `Admin` authorization policy uses `RequireRole("admin")`.

> **Note:** The backend also supports a legacy `"role": "admin"` single-string format for backwards compatibility.

---

## 2. Local Development Setup

### 2.1 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL + local Supabase)
- [Node.js 22+](https://nodejs.org/)

### 2.2 Run Everything (recommended)

```bash
# From the repo root — starts PostgreSQL, local Supabase, backend (with hot reload), and frontend
npm run dev
```

This starts:
- **PostgreSQL** (port 5432) — backend event store
- **Local Supabase** (port 54321) — auth, API gateway, Studio dashboard
- **Backend** (port 5000) — .NET API with hot reload
- **Frontend** (port 4321) — Astro dev server

Press Ctrl+C to stop everything. Run `npm run dev:stop` to stop Docker services.

### 2.3 JetBrains Run Configurations

The `.run/` directory contains shared run configurations:

- **Debug Backend** — launches the .NET backend with debugger attached + Astro dev server. Infrastructure (PostgreSQL + local Supabase) starts automatically as a before-launch step.
- **Watch BE+FE** — launches `dotnet watch` + `astro dev` side-by-side with separate log panels. Both backend and frontend hot-reload on file changes.

### 2.4 Local Supabase

The project includes a `supabase/config.toml` that configures a local Supabase instance with email/password auth (no email confirmation required). On first run, `supabase start` (via `npm run dev:supabase`) downloads the required Docker images (~2-3 min).

Local services:
| Service | URL |
|---------|-----|
| API gateway | `http://localhost:54321` |
| Studio dashboard | `http://localhost:54323` |
| Inbucket (email) | `http://localhost:54324` |

Local credentials (well-known dev values, not secrets):
- **Publishable key**: `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0`

### 2.5 Configure Frontend

The committed `frontend/.env` has local Supabase defaults — ready to use out of the box.

To point at a different Supabase instance, create `frontend/.env.local` (gitignored) to override:
```
PUBLIC_SUPABASE_URL=https://your-project.supabase.co
PUBLIC_SUPABASE_PUBLISHABLE_KEY=your-publishable-key
PUBLIC_API_URL=http://localhost:5000
```

### 2.6 Manual Start (alternative)

If you prefer to start services individually:

```bash
# 1. Start PostgreSQL
cd backend && docker compose up db -d

# 2. Start local Supabase
npm run dev:supabase

# 3. Start backend
cd backend/src/Kalandra.Api
Auth__SupabaseProjectUrl=http://localhost:54321 \
dotnet run
# API at http://localhost:5000, Swagger at /swagger

# 4. Start frontend
cd frontend && npm install && npm run dev
# Available at http://localhost:4321
```

---

## 3. Oracle Cloud Infrastructure (OCI) Setup

### 3.1 Create Always Free VM

1. Sign up for [Oracle Cloud Free Tier](https://cloud.oracle.com/free)
2. Create a Compute instance:
   - Shape: `VM.Standard.A1.Flex` (ARM, 4 OCPU / 24 GB RAM — Always Free)
   - Image: Canonical Ubuntu 24.04 Minimal aarch64 (ARM image for A1 shape)
   - Add your SSH public key
3. Note the **public IP address**

### 3.2 Configure VM

SSH into the instance and install Docker:

```bash
# Ubuntu 24.04
sudo apt update && sudo apt install -y docker.io docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Log out and back in for group changes
exit
```

### 3.3 Configure Firewall

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

### 3.4 Set Up Reverse Proxy (Caddy)

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

### 3.6 DNS Configuration

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
| `SUPABASE_PUBLISHABLE_KEY` | Publishable key from Supabase dashboard (mapped to `PUBLIC_SUPABASE_PUBLISHABLE_KEY` at frontend build time) |

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

---

## 5. Running Tests

### 5.1 Unit/Integration Tests

Tests use Testcontainers (requires Docker):

```bash
cd backend
dotnet test
```

The test suite:
- Spins up a real PostgreSQL container
- Creates a test database
- Generates test JWTs (no Supabase dependency)
- Tests all API endpoints

### 5.2 E2E Testing Notes

Full E2E tests (frontend → backend → DB) are possible because:
- Auth tokens are standard JWTs — tests generate them with a known test secret via `JwtTestHelper`
- The backend validates JWTs via JWKS in production; tests use a symmetric fallback key
- Only the OAuth redirect flow itself requires a real Supabase instance

---

## 6. Architecture Decisions

### Marten Event Sourcing

We use Marten for event sourcing on the job offers feature. Events (`JobOfferSubmitted`, `JobOfferStatusChanged`, `JobOfferCancelled`) are appended to streams. Marten's inline snapshot projections maintain a `JobOffer` read model automatically. The event stream serves as the activity log visible in the UI. Marten manages its own PostgreSQL schema — no migrations needed.

### Admin Role via JWT

Roles are stored as an array in Supabase `app_metadata` (e.g., `"roles": ["admin"]`). The backend extracts `app_metadata.roles` from the JWT on each request and maps each entry to a standard .NET role claim. The `Admin` authorization policy uses `RequireRole("admin")`. No server-side user ID list or database roles table needed — roles live in Supabase and travel with the token. A legacy single-string `"role"` field is also supported.

### Supabase Auth — Local + Production

The backend validates JWT signatures via JWKS (fetching public keys from Supabase's OpenID Connect endpoint). It never calls the Supabase API directly. A symmetric secret fallback exists for tests and local Supabase.

- **Local dev**: `npm run dev:supabase` runs a local Supabase instance in Docker (auth, API gateway, studio). Email/password sign-in works without any external dependencies.
- **Production**: Supabase Cloud with Google OAuth + email/password.
- **E2E tests**: Local Supabase with programmatic user creation via admin API. Tests sign in with `signInWithPassword` — no browser OAuth flows needed.
- **Backend integration tests**: Generate JWTs with a known test secret via Testcontainers. No Supabase dependency.

### Vertical Slices

Code is organized by feature (e.g., `Features/JobOffers/`) rather than by technical layer. Each feature folder contains its controller, events, DTOs, and handlers.

### Testing Strategy

- **Backend integration tests**: xUnit + Testcontainers. Spins up a real PostgreSQL, starts the full API via `WebApplicationFactory`, tests all endpoints with generated JWTs.
- **Frontend page tests**: Playwright. Builds the static site, serves it, verifies page rendering, navigation, i18n, and dark mode.
- **E2E smoke tests**: Playwright against the full stack (frontend + backend + DB). Verifies integration points.
- **CI/CD**: Backend tests run in the backend pipeline; frontend tests run in the frontend pipeline. Both block deployment on failure.
