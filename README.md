# kalandra.tech

Personal showcase website at [www.kalandra.tech](https://www.kalandra.tech). Serves as a demonstration of engineering skills and a playground for new technologies.

## What's here

- [**Home**](https://www.kalandra.tech) — intro and navigation
- [**About Me**](https://www.kalandra.tech/about) — career timeline, manifesto, links
- [**Project**](https://www.kalandra.tech/project) — live roadmap tracking the build progress of this site
- [**Hire Me**](https://www.kalandra.tech/hire-me) — job offer submission form (requires sign-in)
- [**Job Offers**](https://www.kalandra.tech/job-offers) — submitted offers with status tracking and admin review

## Tech stack

- **Frontend**: [Astro](https://astro.build) (SSG) + Tailwind CSS, deployed to Cloudflare Pages
- **Backend**: ASP.NET Core (.NET 10) with Marten (event sourcing), deployed to Oracle Cloud
- **Auth**: Supabase Auth (email/password + Google OAuth)
- **Database**: PostgreSQL (Supabase in production, Docker locally)
- **CI/CD**: GitHub Actions

Architecture decisions and full roadmap are documented in [docs/PROJECT.md](docs/PROJECT.md).

## Development

See [docs/SETUP.md](docs/SETUP.md) for prerequisites, local setup, and deployment infrastructure.
