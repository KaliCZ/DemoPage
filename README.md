# kalandra.tech

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Serves as a demonstration of engineering skills and a playground for new technologies.

## What's here

- [**Home**](https://www.kalandra.tech) — intro and navigation
- [**About Me**](https://www.kalandra.tech/about) — career timeline, values, links
- [**Project**](https://www.kalandra.tech/project) — live roadmap tracking the build progress of this site
- [**Hire Me**](https://www.kalandra.tech/hire-me) — job offer submission form (requires sign-in)
- [**Job Offers**](https://www.kalandra.tech/job-offers) — submitted offers with status tracking and admin review
- [**Blog**](https://www.kalandra.tech/blog) — technical articles with RSS, reactions, and threaded comments
- **MCP endpoint** (`api.kalandra.tech/mcp`) — lets AI assistants submit job offers, browse blog posts, and read/write comments over the [Model Context Protocol](https://modelcontextprotocol.io), served as a route on the API; see [docs/mcp-server.md](docs/mcp-server.md)

## Tech stack

- **Frontend**: [Astro](https://astro.build) (SSG) + Tailwind CSS, deployed to Cloudflare Pages
- **Backend**: ASP.NET Core (.NET 10) with Marten (event sourcing), deployed to Oracle Cloud
- **Auth**: Supabase Auth (email/password + Google OAuth)
- **Database**: PostgreSQL (Supabase in production, Docker locally)
- **Background notifications**: durable store-and-notify emails for blog comments and job offers, delivered by Marten event subscriptions on the async daemon (no separate infrastructure)
- **Observability**: [Sentry](https://sentry.io) for errors, traces, and logs (backend via the OpenTelemetry bridge; frontend via the CDN loader script behind a provider-agnostic abstraction)
- **CI/CD**: GitHub Actions

Architecture decisions, technical roadmap, and the full decision log are documented on the [Project page](https://www.kalandra.tech/project). The page includes goals, an architecture overview diagram, collapsible Architecture Decision Records (ADRs), and a version-by-version roadmap with progress tracking.

## Development

See [docs/SETUP.md](docs/SETUP.md) for prerequisites, local setup, and deployment infrastructure.
