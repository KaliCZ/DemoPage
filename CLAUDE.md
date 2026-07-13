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
npm install                          # Optional — populate node_modules for editors; `aspire run` installs deps itself
aspire run                           # PostgreSQL (Aspire-owned, per-worktree) + local Supabase + backend + frontend, all orchestrated by the Aspire AppHost (dashboard, traces, metrics). Runs `npm install` itself. Supports parallel worktrees via KALANDRA_PORT_OFFSET.
npm test                             # All tests: backend + frontend + E2E
dotnet build                         # Backend only
npm --prefix frontend run build      # Frontend only
```

**When asked to run the app for manual testing, always start the full stack with `aspire run`** — never the frontend dev server alone. Without the backend, every API-backed feature (comments, reactions, job offers) fails with a 502 from the Vite proxy.

**Run `npm test` when a change touches logic or structure.** Content-only changes (blog copy, translations, page text) don't need the suite locally. When you do run tests, run the full suite — no subsets (`dotnet test`, `npm --prefix frontend test`, etc.) as a substitute. Pushing and letting CI run the tests is a viable strategy; what's non-negotiable is a PR left sitting with a failing build — after pushing, check the CI result and fix any failure.

## Design Principles

When making decisions, **choose the approach you'd use in a professional team environment**, not the simplest one that works for the current scale. This project is a showcase of engineering skill — every choice should reflect production-grade thinking.

## Comments

Comments are for the people who read the code the first time. They explain why the code exists — describe some high-level purpose. They don't describe what the algorithm does — the reader can see that. The comments don't assume prior knowledge of the code, they don't assume knowledge of other parts of the codebase — they explain why the code exists and if needed, how it relates to some other part.

**Keep comments simple and short.** One line is almost always enough. Don't write paragraphs, don't restate the code, don't enumerate every edge case — a brief note pointing at the "why" beats a verbose explanation. If a comment is growing past a line or two, it's usually a sign the code itself needs a clearer name or shape.

## GitHub Workflow

**Never close issues directly.** Always create a pull request with `Closes #N` in the PR body and let the merge close the issue. An issue without a merged PR has no reviewable change trail.