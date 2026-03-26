# kalandra.tech — Project Documentation

> **This document is the single source of truth** for project goals, architecture, requirements, and progress.
> The frontend will consume this file directly — do not duplicate this content elsewhere.

## Project Goals

1. **Personal showcase website** — present myself to potential employers/recruiters at [kalandra.tech](https://www.kalandra.tech)
2. **Skills demonstration** — document the building of this project as part of the showcase, including architecture diagrams, decisions, and progress
3. **Technology playground** — experiment with new technologies in a real, deployed project

## Non-Functional Requirements

| Requirement | Details |
|---|---|
| **Automation** | Minimal manual overhead. Infrastructure as code or GitHub Actions for everything. No manual setup steps. |
| **Reliability** | The site must be up reliably. |
| **Cost** | As cheap as possible — preferably free. Backend hosting is the exception (Hetzner VPS ~4 EUR/month). |

---

## Versions & Roadmap

### Version 1 — Static Page
**Status:** Not started

A minimal static site to verify hosting, deployment pipeline, and domain setup.

**Pages:**
- **About Me** — basic personal info, links to GitHub and LinkedIn
- **Project** — architecture design, product requirements, versions, and time tracking (rendered from this document)

**Acceptance criteria:**
- [ ] Static site is deployed and accessible at kalandra.tech
- [ ] CI/CD pipeline deploys on every push to main
- [ ] About page with personal info and links
- [ ] Project page rendering content from this document

---

### Version 2 — Dynamic Content (Light/Dark Mode)
**Status:** Not started

Add JavaScript to enable dynamic UI behavior.

**Features:**
- [ ] Light/dark mode toggle
- [ ] User preference persisted in localStorage

---

### Version 3 — Language Picker
**Status:** Not started

Internationalization with Czech and English. No translation API — both languages are maintained manually.

**Features:**
- [ ] Language picker in the UI (Czech / English)
- [ ] All page content available in both languages
- [ ] Language preference persisted

---

### Version 4 — Login (Supabase Auth)
**Status:** Not started

Integrate Supabase authentication. No new features gated behind login yet.

**Features:**
- [ ] Users can sign up, log in, and log out
- [ ] Password reset / change
- [ ] External login providers (Google at minimum)
- [ ] Logged-in state visible in UI

---

### Version 5 — Backend & Contact Form
**Status:** Not started

Deploy the ASP.NET Core backend connected to Supabase PostgreSQL. First real backend feature: a contact form.

**Features:**
- [ ] ASP.NET Core backend deployed with automated CI/CD
- [ ] Backend validates Supabase JWT tokens
- [ ] Contact form: name, surname, email, message (max 3000 chars)
- [ ] Form submissions stored in PostgreSQL via Marten (event sourcing)
- [ ] Simple form UI on the frontend

---

### Version 6 — Job Offer Form
**Status:** Not started

Upgrade the contact form into a structured job offer submission.

**Fields:** URL, contact info, message, file attachments (up to 30 MB total)

**Job offer states:**
- **Submitted** — initial state after user submits
- **In Review** — owner is reviewing the offer
- **Declined** — offer was declined
- **Accepted** — offer was accepted

**Features:**
- [ ] Job offer submission form with file uploads
- [ ] User can view their submitted offers and current status
- [ ] Status change notifications (Declined / Accepted)
- [ ] SuperAdmin view: all job offers from all users

---

### Version 7 — Emails & Slack
**Status:** Not started

**Features:**
- [ ] Email confirmation on job offer submission (to user)
- [ ] Email notification to site owner on new submission
- [ ] Slack notification to owner on new submission

---

### Version 8 — Observability
**Status:** Not started

**Integrations:**
- [ ] **Sentry** — error tracking, tracing, logging
- [ ] **PostHog** — product analytics
- [ ] **BetterStack** — logging, tracing, uptime monitoring

---

### Version 9 — Background Tasks
**Status:** Not started

Move async work (emails, notifications) out of the request pipeline into durable background processing.

**Features:**
- [ ] Background task processing (emails sent outside the HTTP request)
- [ ] Durable execution with retry semantics
- [ ] Preferred: self-hosted Temporal on Hetzner VPS
- [ ] Fallback: Azure Queue Storage

---

### Version 10 — Pay to Win (Stripe)
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
┌─────────────┐     ┌──────────────────┐     ┌─────────────────────────┐
│  Cloudflare  │     │   Hetzner VPS    │     │       Supabase          │
│              │     │                  │     │                         │
│  Static site │────▶│  ASP.NET Core    │────▶│  PostgreSQL (DB)        │
│  (CDN)       │     │  Backend API     │     │  Auth (JWT)             │
│              │     │                  │     │                         │
│  Domain DNS  │     │  Temporal        │     └─────────────────────────┘
│              │     │  (self-hosted)   │
└─────────────┘     └──────────────────┘
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
| **Framework** | TBD — see [open question](#frontend-framework) | Need to decide between no-build ES6 modules vs. a framework |

### Backend

| Decision | Choice | Rationale |
|---|---|---|
| **Runtime** | ASP.NET Core | Strong typing, familiar, good performance |
| **Hosting** | Hetzner VPS (~4 EUR/month) | Cheapest option that supports long-running processes |
| **Database** | Supabase PostgreSQL | Free tier, managed, includes auth |
| **Event sourcing** | Marten | .NET library, uses PostgreSQL directly |
| **Auth** | Supabase Auth (JWT validation in backend) | Free, includes social logins |

### Background Tasks

| Decision     | Choice | Rationale |
|--------------|---|---|
| **Ideally**  | Temporal (self-hosted on Hetzner) | Durable workflows, free when self-hosted |
| **Fallback** | Azure Queue Storage | If Temporal + Supabase DB causes quota issues |

### CI/CD

| Component | Deployment method |
|---|---|
| **Frontend** | GitHub Actions → Cloudflare Pages (wrangler CLI) |
| **Backend** | GitHub Actions → Hetzner VPS (SSH deploy or Docker) |
| **Infrastructure** | GitHub Actions with respective CLIs per service |

### Observability (Version 8+)

| Tool | Purpose |
|---|---|
| **Sentry** | Error tracking, tracing, logging |
| **PostHog** | Product analytics and feature flags |
| **BetterStack** | Logging, tracing, uptime monitoring |

### Payments (Version 10+)

| Tool | Purpose |
|---|---|
| **Stripe** | Payment processing for premium tiers |

---

## Open Questions

### Frontend Framework

**Context:** Writing vanilla ES6 modules with no build step is appealing for simplicity, but creates friction with Tailwind CSS (which needs a build step to tree-shake unused utilities) and internationalization.

**Options:**
1. **No framework, ES6 modules** — simplest runtime, but need a Tailwind build step anyway. i18n and routing become manual.
2. **SvelteKit** — lightweight, compiles away, good DX, built-in routing and SSR/SSG. Works well with Cloudflare Pages.
3. **Vue.js (Nuxt)** — mature ecosystem, good docs, SSG support.
4. **Astro** — content-focused, supports markdown natively (good for rendering this doc), partial hydration, works with Cloudflare.

**Recommendation:** TBD — to be discussed.

### Temporal + Supabase DB

**Concern:** Temporal requires its own database. Pointing it at the Supabase PostgreSQL might eat into the free tier's connection/storage/egress limits.

**Options:**
1. Use Supabase PostgreSQL for Temporal — monitor quota usage
2. Run a separate SQLite or PostgreSQL instance on the Hetzner VPS for Temporal
3. Fall back to Azure Queue Storage if Temporal overhead is too high

**Recommendation:** TBD — to be discussed.

### Hetzner VPS Automated Deploy

**Feasibility:** Yes, fully possible. Options include:
- **Docker-based:** Build Docker image in GitHub Actions, push to container registry, SSH into VPS to pull and restart
- **Direct deploy:** Build in CI, SCP artifacts to VPS, restart service via SSH
- **Self-hosted runner:** Run a GitHub Actions runner on the VPS (more complex, less recommended)

**Recommendation:** Docker-based deploy is the most robust and reproducible approach.

---

## Time Tracking

| Date | Version | Duration | Description |
|---|---|---|---|
| 2026-03-26 | Setup | — | Initial project setup, documentation |

<!-- Add rows as work progresses. This table will be rendered on the project page. -->
