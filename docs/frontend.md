# Frontend Guide

Astro SSG site with Tailwind CSS, deployed to Cloudflare Pages as static files. Interactivity is either a small vanilla-JS `<script>` block or a Vue 3 island (`@astrojs/vue`) — see "Client-side JavaScript" for when to use which.

## Routing and i18n

Every route page lives in `pages/[...lang]/` and uses Astro's rest parameter to handle all locales from a single file. English gets no prefix (`/about`), Czech gets `/cs/about`.

**Translation file rules:**

- One JSON file per page per language, plus `common.json` for shared strings (nav, footer, auth, a11y).
- Always use raw UTF-8 characters in JSON, never Unicode escapes (`č` not `\u010d`).
- Adding a new language = new JSON files + one entry in the `locales` array in `utils.ts`. No new route files needed.

## Keeping pages small: extract components

**The single most important rule for frontend code quality.** Astro page files should stay focused on routing, data loading, and page-level layout.

**When to extract:**

- A page file exceeds ~300 lines.
- A distinct visual section has its own heading, layout, and internal structure (e.g., a detail card, a form, a diagram).
- The same markup appears on multiple pages.
- A section has enough props/logic that it would benefit from its own `interface Props`.

**How to extract:**

1. Create `src/components/MySection.astro`.
2. Define a `Props` interface in the frontmatter for everything the component needs (translations, data, flags).
3. Move the markup and any scoped `<style>` into the component.
4. Import and use it in the page.

**What stays in the page file:**

- `getStaticPaths()` and locale resolution.
- Top-level data fetching and page-level variables.
- The page's primary layout skeleton (the `<Layout>` wrapper, main sections grid).
- Inline `<script>` tags that wire up page-level interactivity.

**What to avoid:**

- Components that are just thin wrappers around a single HTML element — don't extract for extraction's sake.
- Passing the entire translation object when the component only needs a few strings — pass a focused subset.

## Styling

- **Utility classes in markup** — the default. Use Tailwind classes directly on elements.
- **`theme.css`** — the design system: every `--color-*` and `--font-*` token, light values plus the `.dark` overrides. All app colors (header, footer, forms, code blocks) resolve from here — never hardcode a color elsewhere.
- **`global.css`** — base element resets, font loading, and shared component styling that consume the tokens. Not a dumping ground.
- **Scoped `<style>` in components** — for component-specific styles that can't be expressed as utilities (complex selectors, animations, pseudo-elements).

Avoid a separate CSS file per component. Astro's scoped `<style>` handles isolation.

## Dark mode

Uses `.dark` class toggled on `<html>`. CSS custom properties in `theme.css` are overridden under `.dark`. Flash prevention: `Layout.astro` applies `.no-transitions` on load and removes it after a frame.

All new UI must work in both modes. Use the semantic colour tokens (`bg-background`, `text-on-surface`, etc.) rather than hardcoded colours.

## Client-side JavaScript

Two tiers, chosen by how much state the UI owns:

- **Vanilla `<script>` blocks** — the default for page-level wiring: toggles, fetch-and-render lists, form submission. `<script>` tags go at the bottom of the page or component file, prefer `data-*` attributes for JS hooks, one concern per script block.
- **Vue 3 islands** (`@astrojs/vue`) — for stateful, data-driven widgets where manual DOM bookkeeping would outweigh the framework cost (e.g. `BlogReactions.vue` / `BlogComments.vue` with optimistic updates and threaded rendering). Load with `client:idle` and pass translations and config as props. Don't retrofit existing vanilla pages to Vue without a reason.

Auth plumbing is identical in both tiers: `window.__getAccessToken()`, `window.__getUser()`, `window.__openAuthDialog()`, and the `auth-change` window event.

## Auth on the frontend

Supabase Auth with email/password + Google OAuth. Client-side auth state is managed via `@supabase/supabase-js`.

`Layout.astro` exposes three globals for pages and scripts:

- `window.__supabase` — the Supabase client instance.
- `window.__getAccessToken()` — returns the current JWT (for API calls).
- `window.__getUser()` — returns the current user object.

The sign-in dialog (`AuthDialog.astro`) is included in the layout — pages don't need to add it.

## Blog

Posts are first-class Astro pages, not a CMS — git is the source of truth.

- One `.astro` file per post under `pages/[...lang]/blog/<slug>.astro`; the filename is the slug and also keys the post's reaction/comment streams in the backend (shared across language variants — one discussion per post).
- Every post exports a `metadata` constant (contract in `src/blog/types.ts`) and `export const getStaticPaths = () => blogPostStaticPaths(metadata);`. That metadata drives the index page, the RSS feeds, and the sitemap.
- **A post declares its languages** via `variants` — English-only, Czech-only, or both. Each declared language gets its own title/summary and its own route (`/blog/<slug>`, `/cs/blog/<slug>`); undeclared languages get nothing (no route, no feed item, no sitemap URL, no hreflang claim). Declare a language only when title, summary, AND body are written in it — no half-translations.
- Bilingual bodies live in the same post file, switched with `lang === "cs" ? <Fragment>…</Fragment> : <Fragment>…</Fragment>`; code snippets stay shared (code comments remain English by convention).
- `draft: true` keeps a post in git but out of the build entirely — no page, no feed entry, no sitemap entry.
- Wrap the content in `BlogPostLayout`; use `<BlogCode>` (`src/components/BlogCode.astro`) for snippets — it owns the shared syntax-highlight theme (`src/lib/code-theme.ts`), whose colors are `--code-*` variables that `global.css` aliases to design tokens, so blocks match the palette and flip with `.dark` automatically. Don't pass per-post themes. The layout narrows hreflang to the declared languages and points the language picker at the translated post, or at the blog index when no translation exists.
- The index lists every post at both locales; an entry without a variant in the current language shows its own language's title with a language chip and links across (`lang`/`hreflang` attributes mark the foreign text).
- One feed at `/rss.xml` for the whole blog (identity in `src/blog/feeds.ts`), summary-only, advertised via `<link rel="alternate">` on every page. One item per post: since RSS has no per-item language field, each item title is prefixed with its language tags (`[EN]`, `[CS]`, or `[EN]/[CS]`) and a bilingual post shows both titles (`[EN]/[CS] English / Czech`, default locale first). The item links to its default-locale page (the post carries the language switcher); the description is the default-locale summary. Per-language feeds were tried and dropped — for a mostly-English personal blog, one feed a reader subscribes to once beats making them choose a language edition.

## Sitemap

`/sitemap.xml` is a custom endpoint (`src/pages/sitemap.xml.ts`), not an integration. A static page opts in by exporting `pageMeta` (`src/lib/page-meta.ts`); its `updatedDate` becomes the `<lastmod>` — **bump it when you meaningfully edit a page**. Pages without the export (profile, admin, auth callback) stay out of the sitemap. Blog posts contribute `updatedDate ?? pubDate` from their own metadata and emit URLs only for their declared languages (hreflang alternates only when a post declares more than one); the blog index entry tracks the newest post.

## Accessibility

Non-negotiable baseline for all new UI:

- **Skip link**: `<a href="#main-content">` is in Layout.astro.
- **Navigation**: `aria-current="page"` on the active nav link, `aria-haspopup` on dropdowns, `role="menu"` / `role="menuitem"` for dropdown items.
- **Decorative elements**: `aria-hidden="true"` on icons and decorative images.
- **Forms**: every input gets a visible `<label>` or `aria-label`. Error messages are linked via `aria-describedby`.
- **Colour contrast**: use the design system tokens — they've been chosen for WCAG AA compliance in both light and dark modes.
