# CLAUDE.md — Project Guidelines for AI Assistants

## Project Overview

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Astro SSG frontend with Tailwind CSS, deployed to Cloudflare Pages. ASP.NET Core (.NET 10) backend with Marten (event sourcing) deployed to Oracle Cloud, connecting to Supabase PostgreSQL. Local dev uses Docker PostgreSQL.

See the `/project` page (`frontend/src/pages/[...lang]/project.astro`) for full architecture, roadmap, and decision log.
See `docs/SETUP.md` for setup instructions.
See `docs/frontend.md` for the frontend guide: component extraction, i18n, styling, dark mode, auth, and accessibility.
See `docs/architecture.md` for the bird's-eye view of the backend layers, then `docs/api.md`, `docs/domain.md`, `docs/db.md`, and `docs/testing.md` for per-layer rules and rationale.

## Commands (all from repo root)

```bash
# Install
npm install                         # Root + frontend deps (via postinstall)

# Build
dotnet build                        # Backend — all projects via DemoPage.slnx
npm run build:frontend              # Frontend — Astro static build

# Test
dotnet test                         # Backend integration tests (needs Docker for Testcontainers)
npm run test:frontend               # Frontend Playwright page tests
npm test                            # All tests: backend + frontend + E2E
```

## Design Principles

When making decisions, **choose the approach you'd use in a professional team environment**, not the simplest one that works for the current scale. This project is a showcase of engineering skill — every choice should reflect production-grade thinking.

### i18n Architecture

Per-page translation files (one JSON per page per language) — see `docs/frontend.md` → "Routing and i18n" for the full pattern and rationale.

## Project Structure

Frontend layout is in `docs/frontend.md` → "Directory layout".

```
backend/
  src/
    Kalandra.Api/          # ASP.NET Core Web API
      Infrastructure/      # Auth, Database, CORS setup
      Features/            # Vertical slices (JobOffers, Health)
      Program.cs           # Host builder
  tests/
    Kalandra.Api.Tests/    # Integration tests with Testcontainers
  docker-compose.yml       # PostgreSQL for local dev
supabase/
  config.toml              # Local Supabase config (auth, ports, email settings)
docs/
  SETUP.md                 # Step-by-step setup guide for backend & deployment
  frontend.md              # Frontend guide: components, i18n, styling, auth, a11y
```

### C# Named Arguments

Two rules for method/constructor calls:

1. **Multi-line calls**: When a call spans multiple lines, every argument gets a named parameter.
2. **Opaque literal values**: When passing `null`, `true`, `false`, `0`, `""`, `[]`, or similar literals where the meaning isn't obvious from context, use named parameters. If the meaning is obvious from the variable name (e.g., `userId`, `request`), the name can be omitted on single-line calls.

```csharp
// Good — multi-line, all named
var (success, error, edited) = offer.Edit(
    userId: userId,
    userEmail: userEmail,
    companyName: request.CompanyName,
    timestamp: timeProvider.GetUtcNow());

// Good — single line, null is labeled
var result = await listHandler.HandleAsync(userId: null, page, pageSize, ct);

// Bad — multi-line without names
var (success, error, edited) = offer.Edit(
    userId,
    userEmail,
    request.CompanyName,
    timeProvider.GetUtcNow());

// Bad — null without label
var result = await listHandler.HandleAsync(null, page, pageSize, ct);
```

## Key Conventions

- **Frontend conventions** — see `docs/frontend.md` for routing, i18n, component extraction, styling, dark mode, auth, and accessibility rules.
- **Auth**: Supabase Auth with email/password + Google OAuth. JWT validated on backend via JWKS (public keys fetched from Supabase's OpenID Connect endpoint; symmetric secret fallback for tests). Client-side details in `docs/frontend.md` → "Auth on the frontend".
- **No anonymous objects in API responses.** Controllers must never return `new { error = "..." }` or similar. Use typed error enums with `ModelState`/`ValidationProblem()` for 400s (gives RFC 7807 + traceId), and `Problem()` for 500s. Define a per-feature error enum (e.g., `LinkEmailError`, `CreateJobOfferError`) so the frontend can do direct i18n key lookups. See `docs/api.md` for the controller pattern and full rationale.
- **API error enums are a separate, stable contract.** Handlers return domain/handler error enums (e.g., `UploadAvatarHandlerError`, `CreateJobOfferError`). Controllers map these to API-layer error enums (e.g., `UploadAvatarError`, `CreateOfferError`) via an explicit `switch`. Never pass `result.Error.Get()` directly to `ValidationError()` — a domain enum rename would silently break the frontend's i18n key lookups. See `docs/api.md` → "Error contracts: the two-enum rule".
- **Backend feature code** uses vertical slices: each feature in `Features/{Name}/` with its own controller, DTOs, handlers, and entity configuration. See `docs/api.md` and `docs/domain.md` for the exact layout per layer.
- **Event sourcing**: Marten event store for job offers. Events define state changes, inline projections maintain read models. See `docs/domain.md` for the entity / event / handler pattern and `docs/db.md` for Marten configuration, sessions, streams, and projection lifecycle.
- **Admin role**: Role-based via Supabase `app_metadata.roles` array (e.g., `["admin"]`). Backend extracts roles from JWT and maps each to a .NET role claim. `RequireRole("admin")` authorization policy. See `docs/api.md` → "Authorization" and `docs/db.md` → "Supabase auth integration".
- **Testing**: xUnit v3 with Testcontainers PostgreSQL. API tests use anonymous objects for requests and raw `JsonElement` for responses — never reference contract classes, so renames/changes break the test. See `docs/testing.md` for the full contract-detection rule, test patterns, and helpers.
- **Dev workflow**: `npm run dev` starts PostgreSQL + local Supabase + backend (dotnet watch) + frontend (astro dev). Local Supabase provides auth with email/password sign-in (no email confirmation required).

## Layer Guides

For the long-form rules, rationale, and worked examples behind the bullets above, read the per-layer guides in `docs/`:

- **`docs/frontend.md`** — Frontend guide: Astro component extraction, i18n per-page pattern, Tailwind styling, dark mode, client-side auth, accessibility baseline, and a "where to put new code" cheat sheet.
- **`docs/architecture.md`** — Bird's-eye view: which project owns what (`Kalandra.Api` / `Kalandra.JobOffers` / `Kalandra.Infrastructure`), end-to-end request flow, error flow across layer boundaries, DI registration order, and a "where to put new code" cheat sheet.
- **`docs/api.md`** — API layer rules: thin controllers, vertical slices under `Features/{Name}/`, the two-enum error contract, RFC 7807 validation problems, authorization policies, rate limiting, concurrency wrapping.
- **`docs/domain.md`** — Domain layer rules: the handler pattern, `Try<TSuccess, TError>` return type, entity decide/apply split, event design rules, multi-stream aggregates (job offers + comments), domain error enums.
- **`docs/db.md`** — Database layer: Marten configuration and per-domain `StoreOptions`, `AutoCreate` modes by environment, read vs. write sessions, inline projections, event streams and concurrency control, Supabase auth/storage integrations, health checks.
- **`docs/testing.md`** — Backend testing: the contract-detection rule (anonymous objects + raw JSON, never contract classes), API integration test patterns, domain aggregate tests, concurrency tests, test infrastructure and helpers.

## Build & Deploy

See `docs/SETUP.md` for local development setup, run configurations, test commands, and deployment infrastructure.
See the `/project` page for architecture, tech stack, decision log, and roadmap.
