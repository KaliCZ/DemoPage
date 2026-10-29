# Frontend

## Purpose

The website: content pages in two languages and two themes, plus the interactive parts — sign-in,
the hire-me form, job-offer dashboards, and blog engagement
([requirements](../product-requirements.md#site-and-content)). The coding rules are in
[conventions/frontend.md](../conventions/frontend.md).

## How it works

An Astro site built entirely at build time (SSG) with Tailwind CSS, deployed to Cloudflare Pages as
static files. Everything dynamic calls the backend API from the browser: production builds point
`PUBLIC_API_URL` at `https://api.kalandra.tech`; in development it is empty and Vite proxies
`/api` to the local API.

- **Routing and i18n** — every route page lives in `pages/[...lang]/` and serves all locales from
  one file through Astro's rest parameter. English has no prefix (`/about`); Czech lives under
  `/cs` (`/cs/about`). Adding a language is new translation files plus one entry in the `locales`
  array in `utils.ts`.
- **No trailing slashes** — `trailingSlash: "never"` with `build.format: "file"` makes Cloudflare
  Pages serve `/about` directly and redirect `/about/` to it; home pages are `/` and `/cs`.
- **Client-side code in two tiers:**
  - **Vanilla `<script>` blocks** — page-level wiring and anything that must run at or before
    first paint: the theme and auth-state pre-paint scripts, the layout's global bootstrap,
    visibility toggles driven by `auth-change`, small one-shot handlers.
  - **Vue 3 islands** (`@astrojs/vue`, loaded `client:idle`) — data-driven views: anything that
    fetches server data and renders it, or forms with client-side state and validation (blog
    reactions and comments, the job-offer list and detail).
- **Auth gating** — islands hydrate after first paint, so they never own a page's auth gating: the
  login-prompt and content sections stay static markup toggled by pre-paint CSS tiers
  (`auth-known-in` / `auth-known-out`) plus a vanilla wiring script, and the island loads its data
  on `auth-change`. See [auth.md](auth.md) for the auth plumbing.
- **Sitemap** — `/sitemap.xml` is a custom endpoint (`src/pages/sitemap.xml.ts`). A page opts in
  by exporting `pageMeta` (`src/lib/page-meta.ts`), whose `updatedDate` becomes `<lastmod>`; blog
  posts contribute their own dates and declared languages. Auth-gated shells (profile, admin,
  job-offer detail) stay out of it and pass `noindex` to the layout.
- **CV** — the prebuild step renders `frontend/cv/pavel-kalandra-cv.html` to a PDF with
  Playwright, so the downloadable CV always matches its HTML source.

## Decisions

- **Astro SSG on Cloudflare Pages** — static pages are free to serve with no per-request cost,
  are cached at the edge, ship no JavaScript except the islands, and Astro has i18n routing built
  in. A server-rendered Vue/Nuxt app (on Cloudflare Workers or the VM) is the second-best option
  and the likely move once shared, server-side layout state is worth paying for. Not Next.js,
  which ships a React runtime and is Vercel-aligned; not SvelteKit, which lacks built-in i18n; not
  Vercel's SSR model, which would be a second backend next to ASP.NET Core.
- **Vue islands for data-driven UI** — Vue's template escaping removes the hand-rolled XSS
  surface that HTML built from strings carries.

## Invariants

- Every link, canonical URL, and sitemap entry is built through `localePath`, so all signals agree
  on the slash-less form; mismatched signals get duplicate URLs indexed.
- A vanilla script never assembles HTML from strings; if it needs `innerHTML` and manual escaping
  to render data, it belongs in a Vue island.
