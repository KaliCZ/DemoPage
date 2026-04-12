# CLAUDE.md — Project Guidelines

## Required Reading Before Changes

**MANDATORY: Before writing or editing ANY code, you MUST use the Read tool to read every doc listed in the table below that matches the areas you will touch.** This is not optional. Do not skip this step for "simple" changes. Do not assume you know the conventions — read the actual files first. If you are about to edit a test, read `docs/backend-testing.md`. If you are about to edit a controller, read `docs/backend-api.md` AND `docs/backend-csharp.md` AND `docs/backend-architecture.md`. Read ALL matching docs BEFORE writing any code.

| Changing...                  | Read first                                              |
|------------------------------|---------------------------------------------------------|
| Any backend code             | `docs/backend-csharp.md` + `docs/backend-architecture.md`               |
| Controllers, DTOs, endpoints | `docs/backend-api.md`                                           |
| Entities, events, handlers   | `docs/backend-domain.md`                                        |
| Marten config, DB queries    | `docs/backend-db.md`                                            |
| Tests (writing or changing)  | `docs/backend-testing.md`                                       |
| Frontend pages or components | `docs/frontend.md`                                      |

## About

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). See `docs/SETUP.md` for setup.

## Commands (from repo root)

```bash
npm install            # Root + frontend deps (via postinstall)
npm run dev            # Docker PostgreSQL + local Supabase + backend (dotnet watch) + frontend (astro dev)
npm test               # All tests: backend (dotnet test) + frontend (Playwright) + E2E
dotnet build           # Backend only
npm run build:frontend # Frontend only
```

## Design Principles

When making decisions, **choose the approach you'd use in a professional team environment**, not the simplest one that works for the current scale. This project is a showcase of engineering skill — every choice should reflect production-grade thinking.

## GitHub Workflow

**Never close issues directly.** Always create a pull request with `Closes #N` in the PR body and let the merge close the issue. An issue without a merged PR has no reviewable change trail.