# kalandra.tech — Project Documentation

> Project goals, architecture, requirements, and progress.
> The frontend will consume this file directly — do not duplicate this content elsewhere.

## Project Goals

1. **Personal showcase website** — present myself to potential employers/recruiters at [www.kalandra.tech](https://www.kalandra.tech)
2. **Skills demonstration** — document the building of this project as part of the showcase, including architecture diagrams, decisions, and progress
3. **Technology playground** — experiment with new technologies in a real, deployed project

## Non-Functional Requirements

| Requirement | Details |
|---|---|
| **Automation** | Minimal manual overhead. Infrastructure as code or GitHub Actions for everything. No manual setup steps. |
| **Reliability** | The site must be up reliably. |
| **Cost** | As cheap as possible — preferably free. Backend hosting: Oracle Cloud Always Free (fallback: Hetzner VPS ~€4/month). |

---

## Versions & Roadmap

### Version 1 — Simple Site
**Status:** Done (~7 hours)

Simple site with domain, hosting and deployment pipeline.

**Features:**
- [x] Static site deployed and accessible at [www.kalandra.tech](https://www.kalandra.tech)
- [x] CI/CD pipeline deploys on every push to main
- [x] About page with personal info, manifesto, and career timeline
- [x] Project page with roadmap, tech stack, and quick stats
- [x] Light/dark mode toggle with system preference detection
- [x] User preference persisted in localStorage
- [x] Language picker in the UI (Czech / English)
- [x] All page content available in both languages
- [x] Astro i18n routing (`/` for English, `/cs/` for Czech)
- [x] Responsive layout for mobile and desktop
- [x] SEO-friendly markup with Open Graph and hreflang tags
- [x] Accessible navigation with skip-to-content and ARIA attributes

---

### Version 2 — Backend Included
**Status:** Done (~10 hours)

Supabase Auth with Google OAuth, job offer submission form, and ASP.NET Core backend with Marten event sourcing. Users can submit, track, and cancel offers. Admin can review all submissions and update statuses.

**Job offer states:**
- **Submitted** — initial state after user submits
- **In Review** — owner is reviewing the offer
- **Declined** — offer was declined
- **Accepted** — offer was accepted

**Features:**
- [x] Users can sign in via Google OAuth and email/password
- [x] Signed-in state visible in UI (profile avatar + name in nav)
- [x] Profile dropdown with sign out and "My Submissions" link
- [x] Auth state managed client-side via `@supabase/supabase-js`
- [x] Job offer submission form (Hire Me page in nav)
- [x] Form gated behind authentication (page visible, form disabled until sign-in)
- [x] Full i18n (English + Czech) for all form labels and messages
- [x] Form submits to backend API
- [x] ASP.NET Core Web API with vertical slices architecture
- [x] Marten event sourcing + PostgreSQL (events track full lifecycle)
- [x] Backend validates Supabase JWT tokens with role-based authorization
- [x] Docker Compose for local development (PostgreSQL)
- [x] Dockerfile for production deployment
- [x] Integration tests with Testcontainers
- [x] CI/CD pipeline (GitHub Actions → GHCR → Oracle Cloud SSH deploy)
- [x] User can view their submitted offers and current status
- [x] Admin view: all job offers from all users
- [x] Admin can update offer status and add notes
- [x] Users can cancel their own submissions
- [x] Full activity log (event history) for each job offer
- [x] Playwright frontend tests (page rendering, navigation, dark mode)
- [x] E2E test infrastructure (Playwright against full stack)
- [x] `npm run dev` single-command local development (DB + backend + frontend)
- [ ] Supabase project created and configured (manual step — see `docs/SETUP.md`)
- [ ] Oracle Cloud VM provisioned and configured (manual step — see `docs/SETUP.md`)
- [ ] DNS A record for api.kalandra.tech (manual step)

---

### Version 3 — Emails, Slack & Observability
**Status:** Not started

**Features:**
- [ ] Email confirmation on job offer submission (to user)
- [ ] Email notification to site owner on new submission
- [ ] Slack notification to owner on new submission
- [ ] **Sentry** — error tracking, tracing, logging
- [ ] **PostHog** — product analytics
- [ ] **BetterStack** — logging, tracing, uptime monitoring

---

### Version 4 — Background Tasks
**Status:** Not started

Move async work (emails, notifications) out of the request pipeline into durable background processing.

**Features:**
- [ ] Background task processing (emails sent outside the HTTP request)
- [ ] Durable execution with retry semantics
- [ ] Preferred: self-hosted Temporal on backend VPS
- [ ] Fallback: Azure Queue Storage

---

### Version 5 — Pay to Win (Stripe)
**Status:** Not started

Monetize job offer submissions via Stripe.

**Tiers:**

| Tier | Price | Guarantee |
|---|---|---|
| Free | $0 | Response within 7 days (best effort). Show median response time. |
| Premium | $5 | Guaranteed response within 24 hours |
| Interview (30 min) | $25 | Guaranteed call within 7 days |
| Interview (1 hour) | $50 | Guaranteed call within 7 days |

**Features:**
- [ ] Stripe payment integration
- [ ] Tier selection during submission
- [ ] Median response time displayed for free tier

---

## Technical Architecture

### Overview

```
┌─────────────┐     ┌──────────────────────────┐     ┌─────────────────────────┐
│  Cloudflare  │     │  Oracle Cloud Free Tier  │     │       Supabase          │
│              │     │  (fallback: Hetzner VPS) │     │                         │
│  Static site │────▶│                          │────▶│  PostgreSQL (DB)        │
│  (CDN)       │     │  ASP.NET Core            │     │  Auth (JWT)             │
│              │     │  Backend API              │     │                         │
│  Domain DNS  │     │                          │     └─────────────────────────┘
│              │     │  Temporal (self-hosted)  │
└─────────────┘     └──────────────────────────┘
                              │
                      ┌───────┴────────┐
                      │                │
                ┌─────▼─────┐   ┌─────▼─────┐
                │   Slack    │   │   Email    │
                │   Webhook  │   │   (SMTP)   │
                └───────────┘   └───────────┘
```

### Frontend

| Decision | Choice | Rationale |
|---|---|---|
| **Hosting** | Cloudflare Pages | Free tier, global CDN, automatic HTTPS |
| **Domain/DNS** | Cloudflare | Already using Cloudflare for hosting |
| **CSS** | Tailwind CSS | Utility-first, tree-shakeable |
| **Framework** | Astro (SSG mode) with Vue islands for interactivity | Zero JS by default, built-in i18n, Cloudflare owns Astro. Vue components only where needed. See [ADR](#frontend-framework--astro-ssg-with-vue-islands). |
| **Rendering** | Static Site Generation (SSG) | Build-time only. Cloudflare Pages serves plain HTML/CSS/JS from CDN. No server-side rendering, no Workers. Cache purged automatically on each deploy. |

### Backend

| Decision | Choice | Rationale |
|---|---|---|
| **Runtime** | ASP.NET Core | Strong typing, familiar, good performance |
| **Hosting** | Oracle Cloud Always Free (ARM A1: 4 OCPUs, 24 GB RAM, 200 GB storage). Fallback: Hetzner VPS (~€4/month). | Free, generous specs, supports long-running processes. See [ADR](#backend-hosting--oracle-cloud-always-free-fallback-hetzner-vps) for risks and fallback plan. |
| **Database** | PostgreSQL (local Docker for dev, Supabase PostgreSQL for prod) | Marten manages schema automatically |
| **Event sourcing** | Marten | .NET event sourcing on PostgreSQL. Inline snapshot projections for read models. |
| **Auth** | Supabase Auth (JWT validation in backend) | Free, includes social logins, standard OAuth flow |

### Background Tasks

| Decision     | Choice | Rationale |
|--------------|---|---|
| **Ideally**  | Temporal (self-hosted on the backend VPS) | Durable workflows, free when self-hosted |
| **Fallback** | Azure Queue Storage | If Temporal + Supabase DB causes quota issues |

### CI/CD

| Component | Deployment method |
|---|---|
| **Frontend** | GitHub Actions → Cloudflare Pages (wrangler CLI) |
| **Backend** | GitHub Actions → Oracle Cloud / Hetzner VPS (SSH deploy or Docker) |
| **Infrastructure** | GitHub Actions with respective CLIs per service |

### Observability (Version 3+)

| Tool | Purpose |
|---|---|
| **Sentry** | Error tracking, tracing, logging |
| **PostHog** | Product analytics and feature flags |
| **BetterStack** | Logging, tracing, uptime monitoring |

### Payments (Version 5+)

| Tool | Purpose |
|---|---|
| **Stripe** | Payment processing for premium tiers |

---

## Architecture Decision Log

### Decided

#### Backend Hosting → Oracle Cloud Always Free (fallback: Hetzner VPS)

**Context:** Need a host that supports long-running processes (ASP.NET Core + Temporal background workers). Most free tiers are serverless/scale-to-zero and shut down the process when idle.

**Decision:** Start with Oracle Cloud Always Free ARM A1 instance (4 OCPUs, 24 GB RAM, 200 GB storage). If Oracle proves unreliable, fall back to Hetzner VPS (~€4/month).

**Oracle Cloud — known risks:**
| Risk | Details | Mitigation |
|---|---|---|
| **Signup rejection** | Oracle rejects many free tier signups based on region demand and payment method | Use real credit card, match billing address, pick less popular region |
| **ARM capacity** | Popular regions often have no A1 capacity available | Choose EU or less popular region at signup |
| **Idle reclamation** | Instances with <20% CPU at P95 over 7 days are stopped (not deleted) after 1 week notice | Real workloads (Docker, ASP.NET, Temporal) should stay above threshold. Upgrading to PAYG (still free) disables this entirely. |
| **Account suspension** | Rare reports of accounts suspended without clear reason | Keep PAYG enabled; maintain real usage |

**Setup notes:**
- Use **Ubuntu 24.04 Minimal aarch64** (not Oracle Linux) for straightforward Docker support
- Set a large **boot volume** (~150 GB) at creation to avoid iSCSI block volume complexity
- Open ports in **both** OCI security list AND OS firewall

**Alternatives considered:**
| Option | Why not primary |
|---|---|
| **Hetzner VPS (~€4/month)** | Reliable fallback. Full control, predictable, straightforward deploy. Will switch here if Oracle causes trouble. |
| **Azure App Service Free (F1)** | 60 min CPU/day, no always-on — app sleeps after inactivity. |
| **AWS Lambda / Google Cloud Run** | Serverless — no persistent background process for Temporal workers. |
| **Render free tier** | Spins down after 15 min inactivity. Background workers are paid only. |
| **Fly.io** | Free tier has gotten stingier; likely ends up costing a few dollars anyway with less control than a VPS. |
| **Azure Container Apps** | Free tier is generous, but self-hosting Temporal would be awkward. |

#### Frontend Framework → Astro (SSG) with Vue Islands

**Context:** Need a frontend framework for a mostly-static site hosted on Cloudflare Pages. Requirements: Tailwind CSS, i18n (Czech/English), progressive interactivity (dark mode → login → forms), minimal JS shipped to users.

**Decision:** Start with Astro in static site generation (SSG) mode. Interactive components built as Vue islands, hydrated only where needed. No Cloudflare Workers — Pages serves plain HTML/CSS/JS from CDN. Cache is purged automatically on every deploy.

**Expected evolution:** As more dynamic, authenticated pages are added (v4+: submission detail, user dashboard, admin views), the balance shifts from static content toward SPA-like behavior. Dynamic pages use an SPA fallback pattern — Astro serves a static shell, a Vue island reads the URL, calls the ASP.NET API, and renders content. If this pattern dominates and Astro becomes more hindrance than help, a migration to **Vue/Nuxt** is expected. The Vue components built as islands will transfer directly.

**Why Astro:**
- Ships **zero JS by default** — only interactive islands include client-side code
- **Built-in i18n** routing and locale detection (since v4.3)
- **Cloudflare acquired Astro** (Jan 2026) — best-in-class Pages integration
- SSG output is just HTML/CSS/JS files — the frontend stays a pure static client, all API calls go to the ASP.NET Core backend
- `.astro` template syntax feels similar to HTML/Vue — low learning curve

**Why Vue for islands (not React):**
- HTML-first template authoring (write HTML, enhance with directives) vs React's JS-first approach (write functions that return JSX)
- Both support component composition equally — Vue uses slots, React uses children
- Vue skills transfer directly to a future Vue/Nuxt project if heavier UI logic is needed

**Alternatives considered:**
| Option | Why not chosen |
|---|---|
| **Vanilla ES6 modules** | Still needs a Tailwind build step. Would have to manually build routing, i18n, and component system. No real benefit. |
| **Vue/Nuxt (full app)** | Ships ~50-80 KB Vue runtime to every page even when content is static. Overkill for a mostly-static site. |
| **React/Next.js (full app)** | Ships ~80-120 KB React runtime. JS-first authoring style is less preferred. Next.js is Vercel-aligned, not Cloudflare-aligned. |
| **SvelteKit** | Good framework, but i18n is still not built-in. More geared toward interactive apps than content-driven sites. |
| **Vercel (Next.js/Nuxt/etc.)** | Popular hosting platform, but its SSR model runs server-side functions — effectively a second backend. The goal is a thin static client deployed to a CDN with a single ASP.NET Core backend handling all server logic. Vercel blurs that boundary. |

#### Marten Event Sourcing for Job Offers

**Context:** Job offers have a lifecycle (Submitted → InReview → Accepted/Declined/Cancelled) where tracking the full history of changes is valuable for both the user and admin.

**Decision:** Use Marten with event sourcing for job offer state management.

**Why:**
- Events provide a natural audit trail — every status change, cancellation, and note is captured immutably
- The activity log (event stream) is a first-class feature, not a bolted-on audit table
- Marten's inline snapshot projections automatically maintain read models (current state) with zero extra code
- Marten uses PostgreSQL directly — same database, no additional infrastructure
- The `Apply()` method pattern makes state transitions explicit and testable

#### Admin Role via JWT

**Context:** The app needs role-based authorization (regular users vs admin) without maintaining a separate roles table in the backend.

**Decision:** Roles are stored as an array in Supabase `app_metadata` (e.g., `"roles": ["admin"]`). The backend extracts `app_metadata.roles` from the JWT on each request and maps each entry to a standard .NET role claim. The `Admin` authorization policy uses `RequireRole("admin")`.

**Why:**
- No server-side user ID list or database roles table needed — roles live in Supabase and travel with the token
- Standard .NET authorization infrastructure (`RequireRole`) works out of the box
- Adding new roles = updating `app_metadata` in Supabase, no backend changes
- A legacy single-string `"role"` field is also supported for backwards compatibility

#### Supabase Auth — Local + Production

**Context:** The backend needs to validate Supabase JWTs, and local development needs to work without an internet connection or external Supabase project.

**Decision:** The backend validates JWT signatures via JWKS (fetching public keys from Supabase's OpenID Connect endpoint). It never calls the Supabase API directly. A symmetric secret fallback exists for tests and local Supabase.

**How it works across environments:**
- **Local dev**: `npm run dev:supabase` runs a local Supabase instance in Docker (auth, API gateway, studio). Email/password sign-in works without any external dependencies.
- **Production**: Supabase Cloud with Google OAuth + email/password.
- **E2E tests**: Local Supabase with programmatic user creation via admin API. Tests sign in with `signInWithPassword` — no browser OAuth flows needed.
- **Backend integration tests**: Generate JWTs with a known test secret via Testcontainers. No Supabase dependency.

#### Vertical Slices

**Context:** Need a backend code organization strategy that keeps related code together and avoids the "change one feature, touch five layers" problem.

**Decision:** Code is organized by feature (e.g., `Features/JobOffers/`) rather than by technical layer. Each feature folder contains its controller, events, DTOs, and handlers.

**Why:**
- Adding a new feature = adding a new folder with all its files, no changes to shared layers
- Each feature is self-contained — easy to understand, review, and test in isolation
- Avoids the typical layered architecture problem where a single change touches `Controllers/`, `Services/`, `Models/`, `DTOs/`, etc.

#### Testing Strategy

**Context:** Need a testing approach that provides high confidence with minimal mocking, covering the full stack from API endpoints to database.

**Decisions:**
- **Backend integration tests**: xUnit + Testcontainers. Spins up a real PostgreSQL container, starts the full API via `WebApplicationFactory`, tests all endpoints with generated JWTs. No mocks — tests exercise the real database, real event sourcing, and real auth validation.
- **Frontend page tests**: Playwright. Builds the static site, serves it, verifies page rendering, navigation, i18n, and dark mode.
- **E2E smoke tests**: Playwright against the full stack (frontend + backend + DB). Verifies integration points between frontend and backend.
- **CI/CD**: Backend tests run in the backend pipeline; frontend tests run in the frontend pipeline. Both block deployment on failure.

---

## Open Questions

### Temporal + Supabase DB

**Concern:** Temporal requires its own database. Pointing it at the Supabase PostgreSQL might eat into the free tier's connection/storage/egress limits.

**Options:**
1. Use Supabase PostgreSQL for Temporal — monitor quota usage
2. Run a separate SQLite or PostgreSQL instance on the Hetzner VPS for Temporal
3. Fall back to Azure Queue Storage if Temporal overhead is too high

**Recommendation:** TBD — to be discussed.

---

## Time Tracking

| Date | Version | Duration | Description |
|---|---|---|---|
| 2026-03-26 | Setup | — | Initial project setup, documentation |
| 2026-03-27 | v1 | ~7 hours | Static site, i18n, dark mode, language picker, SEO, accessibility |
| 2026-03-28 – 2026-04-01 | v2 | ~20 hours | Auth (Supabase + Google OAuth), job offer form, ASP.NET Core backend with Marten event sourcing, CI/CD, Docker, integration + E2E tests |

<!-- Add rows as work progresses. This table will be rendered on the project page. -->
