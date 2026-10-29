# Tooling

## Toolchain

Install these before anything else; nothing in the repo installs them:

- .NET SDK 10 (`global.json` pins the feature band and the Microsoft.Testing.Platform runner)
- The Aspire CLI: `dotnet tool install --global aspire.cli`
- Node.js 22 (CI pins the exact version) and npm
- Docker, for the local Supabase stack, the Aspire PostgreSQL, and Testcontainers

## Stack

| Concern | Tool | Notes |
| --- | --- | --- |
| Backend | ASP.NET Core on .NET 10 | Nullable enabled; exhaustive enum switches are errors (`backend/.editorconfig`). |
| Event store | Marten on PostgreSQL | |
| Domain primitives | `Kalicz.StrongTypes` | `NonEmptyString`, `Email`, `Result<T, TError>` — see `docs/conventions/csharp.md`. |
| MCP | `ModelContextProtocol.AspNetCore` | |
| Local orchestration | .NET Aspire AppHost | `aspire/Kalandra.AppHost`. |
| Backend tests | xUnit v3, Testcontainers | |
| Frontend | Astro (SSG), Vue 3 islands | |
| Styling | Tailwind CSS v4 | CSS-first config through `@tailwindcss/vite`; tokens in `src/styles/theme.css`. |
| Auth client | `@supabase/supabase-js` | |
| Frontend tests | Playwright | Page tests and full-stack E2E. |
| Formatting | Prettier | Frontend only. |
| Observability | Sentry (`Sentry.AspNetCore`, `@sentry/browser`) | |

Exact versions are in the `.csproj` files and `package.json` files.

## Your training data is out of date

The installed versions differ from what you may remember. Read the installed version's docs and
mirror what the project already does:

- **Tailwind CSS v4** — configuration is CSS-first (`@import "tailwindcss"` plus `@theme`), with no
  `tailwind.config.js`. Don't reintroduce v3 patterns.
- **Astro** — check the installed major's routing, `client:*` directives, and integration APIs
  before relying on memory.
- **Aspire** — the AppHost API changes between majors; follow the existing `AppHost.cs`.

## Commands

All from the repo root.

| Command | Purpose |
| --- | --- |
| `npm install` | Install root and frontend dependencies and point git at `frontend/.githooks`. `aspire run` also does this. |
| `aspire run` | Run the whole stack locally. |
| `npm test` | Every test tier: backend, frontend pages, then E2E. |
| `dotnet build` | Build the backend. |
| `dotnet test` | Backend unit and integration tests (Docker required). |
| `npm --prefix frontend run build` | Build the static site (also renders the CV PDF with Playwright). |
| `npm --prefix frontend test` | Frontend page tests against the built site. |
| `npm run test:e2e` | Full-stack E2E: Supabase, the API, the built site, and Playwright. |
| `npm --prefix frontend run format` / `format:check` | Apply / verify Prettier. |

The pre-commit hook formats staged frontend files with Prettier.

## CI

`.github/workflows/ci-cd.yml` runs on every PR and push to `main`: `backend-test`, `frontend-test`,
`frontend-format`, and `e2e` in parallel; on `main`, `backend-deploy`, `mcp-deploy`, and
`frontend-deploy` follow once they pass. Dependabot updates GitHub Actions, NuGet, npm, and
Docker images weekly.
