# Frontend — kalandra.tech

Astro SSG frontend with Tailwind CSS, deployed to Cloudflare Pages.

## Commands

```bash
npm install             # Install dependencies
npm run dev             # Dev server at localhost:4321
npm run build           # Build to ./dist/
npm run preview         # Preview production build locally
```

## Structure

```
src/
  i18n/                 # Translation files + utilities
    utils.ts            # Locale type, localePath(), alternateLangUrl()
    en/*.json           # English translations (per page)
    cs/*.json           # Czech translations (per page)
  layouts/
    Layout.astro        # Shell: nav, footer, SEO, dark mode
  pages/
    [...lang]/          # Dynamic routes — one file per page, all locales
      index.astro       # Home (/, /cs/)
      about.astro       # About (/about, /cs/about)
      project.astro     # Project (/project, /cs/project)
  styles/
    global.css          # Tailwind + CSS custom properties + dark mode
```

See [CLAUDE.md](../CLAUDE.md) for conventions and [PROJECT.md](../docs/PROJECT.md) for architecture decisions.
