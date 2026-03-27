# kalandra.tech — Project Documentation

> **This document is the single source of truth** for project goals, architecture, requirements, and progress.
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

### Version 2 — Login (Supabase Auth)
**Status:** Not started

Integrate Supabase authentication. No new features gated behind login yet.

**Features:**
- [ ] Users can sign up, log in, and log out
- [ ] Password reset / change
- [ ] External login providers (Google at minimum)
- [ ] Logged-in state visible in UI

---

### Version 3 — Job Offer Form
**Status:** Not started

Structured job offer submission page. The page is visible to everyone, but the form requires sign-in to interact with. Frontend only — no backend yet, so nothing happens after submission.

**Features:**
- [ ] Job offer submission form with file uploads (up to 30 MB)
- [ ] Form gated behind authentication (page visible to all)
- [ ] Frontend-only — no backend processing yet

---

### Version 4 — Backend & Form Handling
**Status:** Not started

ASP.NET Core backend on Oracle Cloud. Handles job offer form submissions. No frontend changes.

**Job offer states:**
- **Submitted** — initial state after user submits
- **In Review** — owner is reviewing the offer
- **Declined** — offer was declined
- **Accepted** — offer was accepted

**Features:**
- [ ] ASP.NET Core backend deployed with automated CI/CD
- [ ] Backend validates Supabase JWT tokens
- [ ] Form submissions stored in PostgreSQL via Marten (event sourcing)
- [ ] User can view their submitted offers and current status
- [ ] SuperAdmin view: all job offers from all users

---

### Version 5 — Emails, Slack & Observability
**Status:** Not started

**Features:**
- [ ] Email confirmation on job offer submission (to user)
- [ ] Email notification to site owner on new submission
- [ ] Slack notification to owner on new submission
- [ ] **Sentry** — error tracking, tracing, logging
- [ ] **PostHog** — product analytics
- [ ] **BetterStack** — logging, tracing, uptime monitoring

---

### Version 6 — Background Tasks
**Status:** Not started

Move async work (emails, notifications) out of the request pipeline into durable background processing.

**Features:**
- [ ] Background task processing (emails sent outside the HTTP request)
- [ ] Durable execution with retry semantics
- [ ] Preferred: self-hosted Temporal on backend VPS
- [ ] Fallback: Azure Queue Storage

---

### Version 7 — Pay to Win (Stripe)
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
| **Database** | Supabase PostgreSQL | Free tier, managed, includes auth |
| **Event sourcing** | Marten | .NET library, uses PostgreSQL directly |
| **Auth** | Supabase Auth (JWT validation in backend) | Free, includes social logins |

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

### Observability (Version 5+)

| Tool | Purpose |
|---|---|
| **Sentry** | Error tracking, tracing, logging |
| **PostHog** | Product analytics and feature flags |
| **BetterStack** | Logging, tracing, uptime monitoring |

### Payments (Version 7+)

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
- Use **Ubuntu** (not Oracle Linux) for straightforward Docker support
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

<!-- Add rows as work progresses. This table will be rendered on the project page. -->
