# Setup Guide — kalandra.tech v2–v4

Step-by-step guide for setting up the backend, auth, and deployment infrastructure.

---

## 1. Supabase Project Setup

### 1.1 Create Supabase Project

1. Go to [supabase.com](https://supabase.com) and create a new project
2. Note these values from **Settings → API**:
   - **Project URL** (e.g., `https://abcdef.supabase.co`)
   - **anon/public key** (safe for browser)
   - **JWT Secret** (from Settings → API → JWT Settings → JWT Secret)

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

After signing in for the first time, find your Supabase User ID:

1. Go to **Authentication → Users** in the Supabase dashboard
2. Find your user and copy the **User UID** (a UUID like `d4a3b2c1-...`)
3. Add this UUID to the backend config (see section 3.2)

This user will have admin privileges (can see all submissions, update statuses).

**Alternative — SQL approach**:
```sql
-- Run in Supabase SQL Editor to set admin via app_metadata
UPDATE auth.users
SET raw_app_meta_data = raw_app_meta_data || '{"role": "admin"}'::jsonb
WHERE email = 'your@email.com';
```

---

## 2. Local Development Setup

### 2.1 Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL)
- [Node.js 22+](https://nodejs.org/)

### 2.2 Start PostgreSQL

```bash
cd backend
docker compose up db -d
```

This starts PostgreSQL on port 5432 with:
- User: `kalandra`
- Password: `kalandra_dev`
- Database: `kalandra`

### 2.3 Configure Backend

```bash
cd backend/src/Kalandra.Api

# Edit appsettings.Development.json with your Supabase values:
# - Auth.SupabaseProjectUrl
# - Auth.SupabaseJwtSecret (from Supabase dashboard → Settings → API → JWT Secret)
# - Auth.AdminUserIds (array of Supabase user UUIDs)
```

### 2.4 Run Backend

```bash
cd backend/src/Kalandra.Api
dotnet run
# API available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

Marten auto-creates database schema in Development mode (tables for event streams, projections, etc.).

### 2.5 Run Everything (recommended)

```bash
# From the repo root — starts PostgreSQL, backend (with hot reload), and frontend
make dev
```

This runs `docker compose up db`, `dotnet watch run`, and `npm run dev` in parallel.
Press Ctrl+C to stop everything.

### 2.6 Configure Frontend

```bash
cd frontend
cp .env.example .env
# Edit .env with your Supabase public values:
# - PUBLIC_SUPABASE_URL
# - PUBLIC_SUPABASE_ANON_KEY
# - PUBLIC_API_URL=http://localhost:5000
```

### 2.7 Run Frontend

```bash
cd frontend
npm install
npm run dev
# Available at http://localhost:4321
```

### 2.8 Full Docker Stack (optional)

To run everything in Docker (no hot reload):

```bash
cd backend
cp .env.example .env
# Edit .env with Supabase values
docker compose --profile full up
```

---

## 3. Oracle Cloud Infrastructure (OCI) Setup

### 3.1 Create Always Free VM

1. Sign up for [Oracle Cloud Free Tier](https://cloud.oracle.com/free)
2. Create a Compute instance:
   - Shape: `VM.Standard.A1.Flex` (ARM, 4 OCPU / 24 GB RAM — Always Free)
   - Image: Oracle Linux 9 or Ubuntu 22.04
   - Add your SSH public key
3. Note the **public IP address**

### 3.2 Configure VM

SSH into the instance and install Docker:

```bash
# Oracle Linux 9
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Install Docker Compose plugin
sudo dnf install -y docker-compose-plugin

# Log out and back in for group changes
exit
```

### 3.3 Configure Firewall

```bash
# Open port 8080 (API) and 443 (HTTPS via reverse proxy)
sudo firewall-cmd --permanent --add-port=8080/tcp
sudo firewall-cmd --permanent --add-port=443/tcp
sudo firewall-cmd --permanent --add-port=80/tcp
sudo firewall-cmd --reload
```

Also add ingress rules in OCI Console:
- **Networking → Virtual Cloud Networks → Security Lists**
- Add ingress rules for ports 80, 443, 8080

### 3.4 Set Up PostgreSQL on VM

```bash
# Run PostgreSQL in Docker on the VM
docker run -d \
  --name kalandra-db \
  --restart unless-stopped \
  -e POSTGRES_USER=kalandra \
  -e POSTGRES_PASSWORD=<STRONG_PASSWORD> \
  -e POSTGRES_DB=kalandra \
  -v pgdata:/var/lib/postgresql/data \
  -p 5432:5432 \
  postgres:17-alpine
```

### 3.5 Set Up Reverse Proxy (Caddy)

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
| `DB_CONNECTION_STRING` | `Host=localhost;Database=kalandra;Username=kalandra;Password=<STRONG_PASSWORD>` |
| `SUPABASE_PROJECT_URL` | `https://your-project.supabase.co` |
| `SUPABASE_JWT_SECRET` | JWT secret from Supabase dashboard |
| `ADMIN_USER_ID` | Your Supabase user UUID |

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
- Auth tokens are standard JWTs — tests can generate them with the known JWT secret
- The backend validates JWTs independently (no Supabase API calls)
- Only the OAuth redirect flow itself requires a real Supabase instance

---

## 6. Architecture Decisions

### Marten Event Sourcing

We use Marten for event sourcing on the job offers feature. Events (`JobOfferSubmitted`, `JobOfferStatusChanged`, `JobOfferCancelled`) are appended to streams. Marten's inline snapshot projections maintain a `JobOffer` read model automatically. The event stream serves as the activity log visible in the UI. Marten manages its own PostgreSQL schema — no migrations needed.

### Admin Role via Config

Admin users are identified by their Supabase User ID in `appsettings.json` (or environment variables). This is simpler than a database roles table for a single-admin site. The admin check happens in the authorization policy.

### Supabase Auth — No Local Docker

The backend only validates JWT signatures. It never calls the Supabase API directly. Therefore:
- **Local dev**: Use a real Supabase project (free tier). Configure the JWT secret in appsettings.
- **Tests**: Generate JWTs with a known test secret. No Supabase dependency in CI.
- This avoids running Supabase's 12+ Docker containers locally.

### Vertical Slices

Code is organized by feature (e.g., `Features/JobOffers/`) rather than by technical layer. Each feature folder contains its controller, events, DTOs, and handlers.

### Testing Strategy

- **Backend integration tests**: xUnit + Testcontainers. Spins up a real PostgreSQL, starts the full API via `WebApplicationFactory`, tests all endpoints with generated JWTs.
- **Frontend page tests**: Playwright. Builds the static site, serves it, verifies page rendering, navigation, i18n, and dark mode.
- **E2E smoke tests**: Playwright against the full stack (frontend + backend + DB). Verifies integration points.
- **CI/CD**: Backend tests run in the backend pipeline; frontend tests run in the frontend pipeline. Both block deployment on failure.
