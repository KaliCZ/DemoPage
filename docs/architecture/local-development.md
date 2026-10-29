# Local Development

## Purpose

Run the whole system on a laptop with no cloud account and nothing to configure, so every change
can be checked against the real code paths before it ships.

## How it works

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), the
[Aspire CLI](https://aspire.dev/), [Docker](https://docs.docker.com/get-docker/), and
[Node.js 22+](https://nodejs.org/).

```bash
dotnet tool install --global aspire.cli
```

```bash
aspire run
```

`aspire run` launches the AppHost (`aspire/Kalandra.AppHost`), which:

1. runs `npm install` for the root and the frontend;
2. runs `supabase start`, bringing up the Supabase CLI stack for auth, storage, and mail;
3. starts an Aspire-owned PostgreSQL for the Marten event store, the API, the MCP host, and the
   Astro dev server;
4. prints the website and Aspire dashboard URLs. The dashboard has every resource's logs, traces,
   and metrics, plus links to Supabase Studio and Mailpit.

| Stand-in | Replaces | Notes |
| --- | --- | --- |
| Aspire PostgreSQL container | Supabase PostgreSQL (event store) | One per worktree, on a named volume `kalandra-pgdata-<hash of the checkout path>` that survives restarts. |
| Supabase CLI stack | Supabase Auth and Storage | Shared by every worktree on the machine and left running after `Ctrl+C`; ports are in `supabase/config.toml`. `supabase/seed.sql` seeds it. |
| Supabase CLI's Mailpit | SMTP relay and Supabase's auth mailer | Catches the backend's notifications and the auth emails; open it from the dashboard. |
| Turnstile always-pass test keys | Cloudflare Turnstile | Committed in `frontend/.env` and the API's `appsettings.json`. |
| Vite `/api` proxy | `api.kalandra.tech` | The AppHost forces `PUBLIC_API_URL` empty so the browser calls the API through the dev server. |

The committed configuration — `frontend/.env` and the backend's `appsettings.json` — already
matches the local stack. Override frontend values in `frontend/.env.local` (gitignored) and backend
values with `dotnet user-secrets`.

### Dev account

`supabase/seed.sql` creates `dev@kalandra.local` with the admin role (its password is in that
file) for signing in by hand. E2E tests sign up their own users, so they never depend on it.

### Parallel worktrees

Run `aspire run` in each worktree. The AppHost steps its dashboard and OTLP ports past ones that
are taken (or pins them with `KALANDRA_PORT_OFFSET`), and the API, MCP host, and frontend get
dynamic ports. Each worktree has its own event-store database; auth users and stored files are
shared through the one Supabase stack.

### Stopping and resetting

- `Ctrl+C` stops the AppHost and its containers; the database volume keeps its data.
- `supabase stop` stops the Supabase stack; `supabase stop --no-backup` also wipes its data.
- `supabase db reset` re-creates the Supabase database and re-runs the seed.
- To start the event store from scratch, remove this worktree's `kalandra-pgdata-*` volume while
  the AppHost is stopped.

## Test tiers

`npm test` from the repo root runs all three; CI runs the same tiers as separate jobs.

- **Backend** (`dotnet test`) — unit tests per project with no infrastructure; integration tests
  that host the real API against a Testcontainers PostgreSQL with the async daemon running; and
  HTTP-level tests of the real MCP host. Needs Docker. See
  [conventions/backend-testing.md](../conventions/backend-testing.md).
- **Frontend pages** (`npm --prefix frontend test`) — Playwright against the built static site,
  with no backend.
- **E2E** (`npm run test:e2e`) — starts Supabase, builds the site against a local API, runs the API
  (on the Supabase stack's database, from its `appsettings.json` defaults) and a static server, and
  drives the full flows in Playwright, including emails read back from Mailpit.

## Decisions

- **Aspire, not docker compose** — one command starts every process and container, wires their
  URLs together, and gives a dashboard with traces and logs; per-worktree databases make parallel
  work safe.
- **The Supabase CLI for auth and storage** — it runs the same Supabase services as production, so
  the real auth and storage code paths run locally.
- **A separate PostgreSQL for the event store** — the Supabase stack is shared across worktrees, so
  the event store gets its own database per worktree instead of Supabase's.

## What local doesn't cover

- Google sign-in, unless the Google client id and secret are exported as
  `SUPABASE_AUTH_EXTERNAL_GOOGLE_CLIENT_ID` and `SUPABASE_AUTH_EXTERNAL_GOOGLE_SECRET` before
  `supabase start`.
- The OAuth sign-in for MCP clients — Supabase's OAuth server is off in the local config, so only
  the MCP host's anonymous tier works end to end.
- Cloudflare, Caddy, blue/green releases, and source-map uploads — only production exercises them
  ([deployment.md](deployment.md)).
