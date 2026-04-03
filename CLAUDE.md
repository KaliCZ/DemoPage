# CLAUDE.md — Project Guidelines for AI Assistants

## Project Overview

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Astro SSG frontend with Tailwind CSS, deployed to Cloudflare Pages. ASP.NET Core (.NET 10) backend with Marten (event sourcing) deployed to Oracle Cloud, connecting to Supabase PostgreSQL. Local dev uses Docker PostgreSQL.

See `docs/PROJECT.md` for full architecture, roadmap, and decision log.
See `docs/SETUP.md` for setup instructions.

## Design Principles

When making decisions, **choose the approach you'd use in a professional team environment**, not the simplest one that works for the current scale. This project is a showcase of engineering skill — every choice should reflect production-grade thinking.

### Example: i18n Architecture

We use **per-page translation files** (pattern 2) instead of a single file per language (pattern 1):

```
src/i18n/
  utils.ts                 # Locale type, helper functions
  en/
    common.json            # Nav, footer, theme, auth, a11y strings
    home.json              # Home page content
    about.json             # About page content
    project.json           # Project page content
    hire-me.json           # Hire Me form page content
    job-offers.json        # Job Offers list/detail page content
  cs/
    common.json
    home.json
    about.json
    project.json
    hire-me.json
    job-offers.json
```

**Why pattern 2 over pattern 1?**
- A single file per language works fine for small projects, but doesn't scale to teams or larger codebases
- Per-page files reduce merge conflicts when multiple people edit different pages
- Each file is self-contained — you can find all strings for a page without scrolling through hundreds of keys
- The breaking point for a single file is around 10–15 pages / 200–300 keys, but we chose pattern 2 from the start because this is a portfolio project meant to demonstrate professional practices

**How it works:**
- Route pages use Astro's `[...lang]` rest parameter — a single route file handles all locales via `getStaticPaths()`. English gets no prefix (`/about`), Czech gets `/cs/about`.
- Each route page imports both `en/*.json` and `cs/*.json` and selects based on the resolved `lang` param. No separate component layer needed — each page file is the single source of truth for its UI.
- Layout.astro imports `common.json` for nav, footer, and accessibility strings.
- Adding a new language = adding JSON files + one entry in `locales` array in `utils.ts`. No new route files, no UI changes.

## Project Structure

```
frontend/
  src/
    i18n/                  # Translation files + utilities
      utils.ts             # Locale type, localePath(), alternateLangUrl()
      en/*.json            # English translations (per page)
      cs/*.json            # Czech translations (per page)
    lib/
      supabase.ts          # Supabase config (public env vars, API URL)
    layouts/
      Layout.astro         # Shell: nav, footer, SEO, dark mode, auth UI
    pages/
      [...lang]/           # Dynamic routes — one file per page, all locales
        index.astro        # Home (/, /cs/)
        about.astro        # About (/about, /cs/about)
        project.astro      # Project (/project, /cs/project)
        hire-me.astro      # Hire Me form (/hire-me, /cs/hire-me)
        job-offers.astro   # Job Offers list (/job-offers, /cs/job-offers)
    styles/
      global.css           # Tailwind + CSS custom properties + dark mode
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
  PROJECT.md               # Source of truth for goals, architecture, roadmap
  SETUP.md                 # Step-by-step setup guide for backend & deployment
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

- **UI changes** go in `src/pages/[...lang]/` — one file per page, all languages served from it
- **Content changes** go in `src/i18n/{lang}/` JSON files — always use raw UTF-8 characters, never Unicode escapes (`č` not `\u010d`)
- **Route pages** use `[...lang]` dynamic routes with `getStaticPaths()`. One file per page handles all locales. No separate component layer.
- **Dark mode** uses `.dark` class on `<html>` with CSS custom property overrides. Flash prevention via `.no-transitions` class.
- **Accessibility**: skip-to-content link, aria-current on nav, aria-hidden on decorative elements, aria-haspopup on dropdowns, role="menu"/role="menuitem", role="contentinfo" on footer.
- **Auth**: Supabase Auth with email/password + Google OAuth. JWT validated on backend via JWKS (public keys fetched from Supabase's OpenID Connect endpoint; symmetric secret fallback for tests). Auth state managed client-side via `@supabase/supabase-js`. Layout.astro exposes `window.__supabase`, `window.__getAccessToken()`, and `window.__getUser()` for pages. Sign-in dialog supports both email/password and Google OAuth.
- **Backend feature code** uses vertical slices: each feature in `Features/{Name}/` with its own controller, DTOs, handlers, and entity configuration.
- **Event sourcing**: Marten event store for job offers. Events define state changes, inline projections maintain read models.
- **Admin role**: Role-based via Supabase `app_metadata.roles` array (e.g., `["admin"]`). Backend extracts roles from JWT and maps each to a .NET role claim. `RequireRole("admin")` authorization policy. Legacy single-string `"role"` also supported.
- **Testing**: xUnit v3 with Microsoft.Testing.Platform. `global.json` in `backend/` configures the test runner.
- **Dev workflow**: `npm run dev` starts PostgreSQL + local Supabase + backend (dotnet watch) + frontend (astro dev). Local Supabase provides auth with email/password sign-in (no email confirmation required).

## Build & Deploy

See `docs/SETUP.md` for local development setup, run configurations, test commands, and deployment infrastructure.
See `docs/PROJECT.md` for architecture, tech stack, decision log, and roadmap.
