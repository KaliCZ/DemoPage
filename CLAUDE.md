# CLAUDE.md — Project Guidelines for AI Assistants

## Project Overview

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Astro SSG frontend with Tailwind CSS, deployed to Cloudflare Pages. ASP.NET Core backend with EF Core + PostgreSQL, deployed to Oracle Cloud.

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
docs/
  PROJECT.md               # Source of truth for goals, architecture, roadmap
  SETUP.md                 # Step-by-step setup guide for backend & deployment
```

## Key Conventions

- **UI changes** go in `src/pages/[...lang]/` — one file per page, all languages served from it
- **Content changes** go in `src/i18n/{lang}/` JSON files — always use raw UTF-8 characters, never Unicode escapes (`č` not `\u010d`)
- **Route pages** use `[...lang]` dynamic routes with `getStaticPaths()`. One file per page handles all locales. No separate component layer.
- **Dark mode** uses `.dark` class on `<html>` with CSS custom property overrides. Flash prevention via `.no-transitions` class.
- **Accessibility**: skip-to-content link, aria-current on nav, aria-hidden on decorative elements, aria-haspopup on dropdowns, role="menu"/role="menuitem", role="contentinfo" on footer.
- **Auth**: Supabase Auth with OAuth (Google). JWT validated on backend. Auth state managed client-side via `@supabase/supabase-js`. Layout.astro exposes `window.__getAccessToken()` and `window.__getUser()` for pages.
- **Backend feature code** uses vertical slices: each feature in `Features/{Name}/` with its own controller, DTOs, handlers, and entity configuration.
- **Admin role**: Configured via `Auth.AdminUserIds` in appsettings (list of Supabase user UUIDs).

## Build & Deploy

### Frontend
```bash
cd frontend
npm install
npx astro build          # Output: frontend/dist/
npx astro dev            # Dev server: http://localhost:4321
```

### Backend
```bash
cd backend
docker compose up db -d  # Start PostgreSQL
cd src/Kalandra.Api
dotnet run               # API at http://localhost:5000, Swagger at /swagger
```

### Tests
```bash
cd backend
dotnet test              # Requires Docker (Testcontainers)
```

Frontend deployed via GitHub Actions → Cloudflare Pages on push to main.
Backend deployed via GitHub Actions → Docker image → Oracle Cloud on push to main.
