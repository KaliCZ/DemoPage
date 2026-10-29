# kalandra.tech

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech) — a demonstration of
engineering skills and a playground for new technologies, built in public.

## What's here

- [**Home**](https://www.kalandra.tech) — intro and navigation
- [**About Me**](https://www.kalandra.tech/about) — career timeline, values, links
- [**Projects**](https://www.kalandra.tech/projects) — this site and [StrongTypes](https://www.kalandra.tech/strong-types), a C# NuGet package for safer types
- [**Project**](https://www.kalandra.tech/project) — how this site is built: goals, architecture, decisions, and roadmap
- [**Hire Me**](https://www.kalandra.tech/hire-me) — job offer submission (requires sign-in)
- [**Job Offers**](https://www.kalandra.tech/job-offers) — submitted offers with status tracking and admin review
- [**Blog**](https://www.kalandra.tech/blog) — technical articles with RSS, reactions, and threaded comments
- [**MCP**](https://www.kalandra.tech/mcp) — connect an AI assistant at `https://mcp.kalandra.tech/mcp` to browse the blog, submit job offers, and comment over the [Model Context Protocol](https://modelcontextprotocol.io)

## Tech stack

- **Frontend**: [Astro](https://astro.build) (static) + Tailwind CSS + Vue islands, on Cloudflare Pages
- **Backend**: ASP.NET Core (.NET 10) with Marten event sourcing — a REST API and a separate MCP host — on an Oracle Cloud VM
- **Auth, database, storage**: Supabase (email/password + Google sign-in; PostgreSQL)
- **Notifications**: durable emails delivered by Marten event subscriptions
- **Observability**: Sentry for errors, traces, and logs on both backend and frontend
- **CI/CD**: GitHub Actions with blue/green deploys

## Development

With the .NET 10 SDK, the Aspire CLI, Docker, and Node.js 22 installed:

```bash
aspire run
```

That starts the whole stack — Supabase, the database, the API, the MCP host, and the site — and
prints the website and dashboard URLs. Sign in with the seeded `dev@kalandra.local` account
(password in `supabase/seed.sql`). `npm test` runs every test tier.

- [Local development](docs/architecture/local-development.md) — the local stack, test tiers, and resets
- [Architecture](docs/architecture/README.md) — how the system is built and why
- [Product requirements](docs/product-requirements.md) — what the site must do
- [Deployment](docs/architecture/deployment.md) — hosting, CI/CD, and one-time setup
- [TECH.md](TECH.md) — tooling and commands
