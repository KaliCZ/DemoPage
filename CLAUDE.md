# CLAUDE.md — Project Guidelines for AI Assistants

## Project Overview

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Astro SSG frontend with Tailwind CSS, deployed to Cloudflare Pages. ASP.NET Core (.NET 10) backend with Marten (event sourcing) deployed to Oracle Cloud, connecting to Supabase PostgreSQL. Local dev uses Docker PostgreSQL.

See the `/project` page (`frontend/src/pages/[...lang]/project.astro`) for full architecture, roadmap, and decision log.
See `docs/SETUP.md` for setup instructions.

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

## Required Reading Before Changes

**Always read the relevant guide before modifying code.** Do not assume content or conventions — read the actual docs first.

| Changing...                  | Read first                                              |
|------------------------------|---------------------------------------------------------|
| Any backend code             | `docs/backend-csharp.md` + `docs/backend-architecture.md`               |
| Controllers, DTOs, endpoints | `docs/backend-api.md`                                           |
| Entities, events, handlers   | `docs/backend-domain.md`                                        |
| Marten config, DB queries    | `docs/backend-db.md`                                            |
| Tests (writing or changing)  | `docs/backend-testing.md`                                       |
| Frontend pages or components | `docs/frontend.md`                                      |

## Dev Workflow

`npm run dev` starts PostgreSQL + local Supabase + backend (dotnet watch) + frontend (astro dev). Local Supabase provides auth with email/password sign-in (no email confirmation required). See `docs/SETUP.md` for full setup and deployment.
