# Agent Instructions

kalandra.tech is Pavel Kalandra's personal showcase site, built in public at
[www.kalandra.tech](https://www.kalandra.tech). A static Astro site on Cloudflare Pages talks to an
ASP.NET Core (.NET 10) backend on an Oracle Cloud VM; the backend stores everything as Marten
events in Supabase's PostgreSQL, trusts Supabase Auth, and serves the same domain to AI assistants
through a separate MCP host. The site doubles as a showcase of engineering skill, so choose the
approach you'd use in a professional team, not the simplest one that works at this scale.

## Read the design first

- Product requirements (what and why, no technology): [docs/product-requirements.md](docs/product-requirements.md)
- Architecture overview, technical requirements, and the capability index:
  [docs/architecture/README.md](docs/architecture/README.md)
- Coding rules per layer: [docs/conventions/](docs/conventions/)

**MANDATORY: before writing or editing ANY code, use the Read tool to read every doc in this table
that matches the areas you will touch.** Not optional, including for "simple" changes — don't
assume you know the conventions. Editing a test? Read the testing doc. Editing a controller? Read
the API, C#, and backend docs. Read all matching docs before writing any code.

| Changing...                         | Read first                                                                 |
|-------------------------------------|----------------------------------------------------------------------------|
| Any backend code                    | `docs/conventions/csharp.md` + `docs/architecture/backend.md`              |
| Controllers, DTOs, endpoints        | `docs/conventions/api.md`                                                  |
| Entities, events, handlers          | `docs/conventions/domain.md`                                               |
| Marten config, DB queries, storage  | `docs/architecture/persistence.md`                                         |
| Notification emails                 | `docs/architecture/notifications.md`                                       |
| Auth, roles, sign-in, rate limits   | `docs/architecture/auth.md`                                                |
| MCP server (tools, clients)         | `docs/architecture/mcp-server.md`                                          |
| Backend tests                       | `docs/conventions/backend-testing.md`                                      |
| Frontend pages, components, tests   | `docs/conventions/frontend.md` + `docs/architecture/frontend.md`           |
| Blog posts or blog features         | `docs/architecture/blog.md`                                                |
| AppHost, local stack, seed data     | `docs/architecture/local-development.md`                                   |
| CI/CD, `infra/`, deployment         | `docs/architecture/deployment.md`                                          |
| Sentry, telemetry                   | `docs/architecture/observability.md`                                       |

Commands and tooling:

@TECH.md

## Structure

- `backend/src/` — the .NET projects: the two hosts (`Kalandra.Api`, `Kalandra.McpServer`), their
  shared web composition (`Kalandra.Hosting`), one project per domain (`Kalandra.JobOffers`,
  `Kalandra.Blog`), and `Kalandra.Infrastructure`.
- `backend/tests/` — one test project per backend project.
- `aspire/Kalandra.AppHost/` — the Aspire AppHost that runs the whole stack locally.
- `frontend/` — the Astro site: `src/pages/[...lang]/` routes (blog posts included), `src/components/`,
  `src/lib/`, `src/i18n/<lang>/` translations, `tests/` (Playwright), and `cv/` (the CV source).
- `supabase/` — the local Supabase CLI config and seed.
- `infra/quadlet/` — the API and MCP container units and Caddy fragments the deploy installs.
- `infra/host/` — shared host setup (the Caddy unit, alerting scripts) with its runbooks.
- `docs/` — product requirements, architecture, and conventions.
- `.github/workflows/ci-cd.yml` — the one CI/CD pipeline.

## Run it locally

- `aspire run` from the repo root starts everything: Supabase (auth, storage, Mailpit), the event
  store, the API, the MCP host, and the site, with the Aspire dashboard. **When asked to run the app
  for manual testing, always use `aspire run`** — the frontend dev server alone has no backend, so
  every API-backed feature fails with a 502 from the Vite proxy.
- The seeded account is `dev@kalandra.local` with the admin role; its password is in
  `supabase/seed.sql`.
- Parallel worktrees each run their own `aspire run`.
- Details, stand-ins, and resets: [docs/architecture/local-development.md](docs/architecture/local-development.md).

## Workflow

- Run `npm test` when a change touches logic or structure; content-only changes (blog copy,
  translations, page text) don't need it. When you run tests, run the full suite, not a subset.
  Pushing and letting CI run them is fine — a PR left with a failing build is not.
- Never close issues directly: open a PR with `Closes #N` in its body and let the merge close it.

## House rules

- Domain projects never reference ASP.NET Core, and the two hosts never reference each other —
  [backend.md](docs/architecture/backend.md).
- Controllers and MCP tools hold no business logic and never touch Marten; handlers do —
  [api.md](docs/conventions/api.md).
- Domain error enums never reach the wire: map them to API error enums with an exhaustive `switch`,
  never a `default` arm — [api.md](docs/conventions/api.md#error-contracts-the-two-enum-rule).
- Another user's resource is "not found", never "forbidden" —
  [domain.md](docs/conventions/domain.md#queries-enforce-access-control).
- A shipped event is never removed or renamed; an incompatible change is a new event type —
  [domain.md](docs/conventions/domain.md#events).
- Entities never read the clock; the time comes through the command from `TimeProvider` —
  [domain.md](docs/conventions/domain.md#events).
- Only the API runs the async daemon, and every notification delivery stores its sent marker —
  [notifications.md](docs/architecture/notifications.md).
- A new MCP tool is account-only unless it is deliberately made public —
  [mcp-server.md](docs/architecture/mcp-server.md#invariants).
- API integration tests send anonymous objects and assert on raw JSON —
  [backend-testing.md](docs/conventions/backend-testing.md#the-contract-detection-rule).
- A new blog post is also added to `BlogPostCatalog`, and an existing post's stream id never
  changes — [blog.md](docs/architecture/blog.md#invariants).
- Frontend links go through `localePath`, and vanilla scripts never build HTML from strings —
  [frontend.md](docs/architecture/frontend.md#invariants).
- Colors come only from the `theme.css` tokens, and all UI works in both themes —
  [frontend.md](docs/conventions/frontend.md#styling).
- Secrets never go into git; production configuration comes from GitHub secrets and variables —
  [deployment.md](docs/architecture/deployment.md#github-configuration).

## Comments

A comment states, in one line, a why the code can't show — an intent, a constraint, or a gotcha —
for a reader who knows nothing about the rest of the codebase. No restating the code, no history
of the change.

## Docs are living

The docs describe the system as it is today. A change that alters behavior, architecture, or a
requirement updates the matching doc in the same PR — rewrite it in place, delete what no longer
applies, and never append history ("previously", "superseded", "amended"). Git is the history.
