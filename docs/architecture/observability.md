# Observability

## Purpose

Know when the site breaks and why, without paying for monitoring
([technical requirements](README.md#technical-requirements)).

## How it works

Errors, logs, traces, and session replays go to [Sentry](https://sentry.io), in two projects
because the platforms differ: a .NET project for both backend hosts and a Browser JavaScript
project for the site.

- **Backend** — `Kalandra.Hosting/Observability.cs` sets up `Sentry.AspNetCore` for errors and
  logs, and exports the hosts' OpenTelemetry traces to Sentry over OTLP. Cancelled requests and
  client disconnects are filtered out as noise. The DSN is required in production — the hosts
  refuse to start without it — and optional in development. A `BetterStack` config section, which
  production doesn't set, adds a second trace exporter to BetterStack.
- **Frontend** — `src/lib/observability.ts` dynamic-imports `@sentry/browser` and exposes a thin
  provider-agnostic API (`identifyUser`, `track`) to the rest of the site. The import is gated on
  `PUBLIC_SENTRY_DSN` at build time, so an empty DSN tree-shakes Sentry out of the bundle.
- **Source maps** — `frontend-deploy` runs `@sentry/vite-plugin` after `astro build`, uploading
  the hidden source maps tagged with the deploy commit so production stack traces resolve to the
  original source. `astro.config.mjs` generates source maps only when `SENTRY_AUTH_TOKEN` is set,
  so local and CI builds produce none. Without the token the upload is skipped and the deploy still
  succeeds.
- **Environments** — frontend events carry an `environment` from Vite's build mode (`development`
  under `astro dev`, `production` from `astro build`). CI builds the site with
  `PUBLIC_SENTRY_DSN=""`, so test runs never send events.
- **Locally** — the Aspire dashboard shows the backend's traces, metrics, and logs; the dev
  frontend sends its spans there too (`src/lib/otel-dev.ts`). The frontend DSN is committed, so a
  local session also reports to Sentry's `development` environment; set `PUBLIC_SENTRY_DSN=` in
  `frontend/.env.local` to opt out. To send backend events from a local run, store the backend DSN
  in user secrets:

  ```bash
  read -rsp "Backend Sentry DSN: " SENTRY_DSN && dotnet user-secrets --project backend/src/Kalandra.Api set "Sentry:Dsn" "$SENTRY_DSN"
  ```

## Decisions

- **Sentry for everything** — one tool covers errors, logs, traces, and replays on both platforms,
  and its free tier is enough for this traffic.
- **The frontend DSN is committed** — a browser DSN is a public credential; Sentry's per-project
  Allowed Domains list protects it, not secrecy.
- **The provider stays behind an abstraction** — pages call `identifyUser` and `track`, never the
  Sentry SDK, so swapping the provider touches one file.

## Invariants

- A deliberate refusal (an MCP tool refusing a call, a health probe failing) is not reported as an
  error, so alerts stay meaningful.
- The frontend Sentry project's **Allowed Domains** stays limited to `https://www.kalandra.tech`,
  `http://localhost:*`, and `http://127.0.0.1:*`.

## Setting it up

1. In Sentry, create a **.NET → ASP.NET Core** project and a **JavaScript → Browser** project. The
   organization is in the EU region; the auth token carries its region, so no `SENTRY_URL` is
   needed.
2. On each project's **Client Keys (DSN)** page, set Allowed Domains as above and copy the DSN: the
   backend's goes into the `BACKEND_SENTRY_DSN` secret, the frontend's into `frontend/.env`.
3. Create an organization auth token (scope `org:ci`) for the `SENTRY_CI_TOKEN` secret and set
   `SENTRY_PROJECT` to the frontend project's slug ([deployment.md](deployment.md#github-configuration)).
