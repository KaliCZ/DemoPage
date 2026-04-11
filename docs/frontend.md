# Frontend Guide

Astro SSG site with Tailwind CSS, deployed to Cloudflare Pages as static files. All interactive behaviour is vanilla JS in `<script>` tags — no React, no Vue, no client-side framework.

## Table of contents

- [Directory layout](#directory-layout)
- [Routing and i18n](#routing-and-i18n)
- [Keeping pages small: extract components](#keeping-pages-small-extract-components)
- [Styling](#styling)
- [Dark mode](#dark-mode)
- [Client-side JavaScript](#client-side-javascript)
- [Auth on the frontend](#auth-on-the-frontend)
- [Accessibility](#accessibility)
- [Where to put new code](#where-to-put-new-code)

## Directory layout

```
frontend/src/
  components/              # Reusable Astro components (extracted from pages)
    ArchitectureDiagram.astro
    AuthDialog.astro
    JobOfferDetail.astro
    icons/                 # SVG icon components
  i18n/                    # Translation files + utilities
    utils.ts               # Locale type, localePath(), alternateLangUrl()
    en/*.json              # English translations (per page)
    cs/*.json              # Czech translations (per page)
  layouts/
    Layout.astro           # Shell: nav, footer, SEO, dark mode, auth UI
  lib/
    supabase.ts            # Supabase config (public env vars, API URL)
  pages/
    [...lang]/             # Dynamic routes — one file per page, all locales
  styles/
    global.css             # Tailwind + CSS custom properties + design tokens
```

## Routing and i18n

Every route page lives in `pages/[...lang]/` and uses Astro's rest parameter to handle all locales from a single file. English gets no prefix (`/about`), Czech gets `/cs/about`.

Each page imports both `en/*.json` and `cs/*.json` and selects based on the resolved `lang` param:

```astro
---
import en from '../../i18n/en/about.json';
import cs from '../../i18n/cs/about.json';
const t = lang === 'cs' ? cs : en;
---
<h1>{t.title}</h1>
```

**Translation file rules:**
- One JSON file per page per language, plus `common.json` for shared strings (nav, footer, auth, a11y).
- Always use raw UTF-8 characters in JSON, never Unicode escapes (`č` not `\u010d`).
- Adding a new language = new JSON files + one entry in the `locales` array in `utils.ts`. No new route files needed.

## Keeping pages small: extract components

**The single most important rule for frontend code quality.** Astro page files should stay focused on routing, data loading, and page-level layout. When a section of a page grows complex — has its own markup structure, props, or internal logic — extract it into `src/components/`.

**When to extract:**
- A page file exceeds ~300 lines.
- A distinct visual section has its own heading, layout, and internal structure (e.g., a detail card, a form, a diagram).
- The same markup appears on multiple pages.
- A section has enough props/logic that it would benefit from its own `interface Props`.

**How to extract:**
1. Create `src/components/MySection.astro`.
2. Define a `Props` interface in the frontmatter for everything the component needs (translations, data, flags).
3. Move the markup and any scoped `<style>` into the component.
4. Import and use it in the page: `<MySection t={t} data={data} />`.

**Example — before:**
```astro
<!-- pages/[...lang]/job-offers.astro — 700+ lines -->
---
// ... route setup, data fetching ...
---
<Layout>
  <!-- 100 lines of list markup -->
  <!-- 200 lines of detail card markup -->
  <!-- 150 lines of comment section markup -->
  <!-- 100 lines of form markup -->
</Layout>
```

**After:**
```astro
<!-- pages/[...lang]/job-offers.astro — ~150 lines -->
---
import JobOfferDetail from '../../components/JobOfferDetail.astro';
import JobOfferComments from '../../components/JobOfferComments.astro';
// ...
---
<Layout>
  <!-- List markup stays in-page (it's the page's primary concern) -->
  <JobOfferDetail t={t} offer={offer} isAdmin={isAdmin} />
  <JobOfferComments t={t} comments={comments} />
</Layout>
```

**What stays in the page file:**
- `getStaticPaths()` and locale resolution.
- Top-level data fetching and page-level variables.
- The page's primary layout skeleton (the `<Layout>` wrapper, main sections grid).
- Inline `<script>` tags that wire up page-level interactivity.

**What to avoid:**
- Components that are just thin wrappers around a single HTML element — don't extract for extraction's sake.
- Passing the entire translation object when the component only needs a few strings — pass a focused subset or the specific section of the translation.

## Styling

Tailwind CSS utility classes are the primary styling approach. Design tokens (colours, spacing, typography) are defined as CSS custom properties in `global.css` under `@theme`.

**Where styles go:**
- **Utility classes in markup** — the default for all styling. Use Tailwind classes directly on elements.
- **`global.css`** — design tokens (`--color-*`, `--font-*`), base element resets, and dark mode overrides. This is the design system, not a dumping ground.
- **Scoped `<style>` in components** — for component-specific styles that can't be expressed as utilities (complex selectors, animations, pseudo-elements).

**Avoid** a separate CSS file per component. Astro's scoped `<style>` handles isolation. Global styles belong in `global.css`.

## Dark mode

Uses `.dark` class toggled on `<html>`. CSS custom properties in `global.css` are overridden under `.dark`:

```css
.dark {
  --color-background: #121212;
  --color-surface: #1e1e1e;
  /* ... */
}
```

Flash prevention: Layout.astro applies `.no-transitions` on load and removes it after a frame, so the dark-mode switch doesn't cause a visible transition on page load.

All new UI must work in both modes. Use the semantic colour tokens (`bg-background`, `text-on-surface`, etc.) rather than hardcoded colours.

## Client-side JavaScript

All interactivity is vanilla JS in `<script>` tags — no framework.

**Conventions:**
- `<script>` tags go at the bottom of the page or component file, after the markup.
- Use `document.addEventListener('astro:page-load', ...)` for scripts that need to run after navigation (if View Transitions are ever added).
- Prefer `data-*` attributes for JS hooks over class names or IDs. This separates styling from behaviour.
- Keep scripts focused — one concern per script block. If a page needs multiple independent behaviours, use separate `<script>` tags.

## Auth on the frontend

Supabase Auth with email/password + Google OAuth. Client-side auth state is managed via `@supabase/supabase-js`.

`Layout.astro` exposes three globals for pages and scripts:
- `window.__supabase` — the Supabase client instance.
- `window.__getAccessToken()` — returns the current JWT (for API calls).
- `window.__getUser()` — returns the current user object.

**Making authenticated API calls:**
```js
const token = await window.__getAccessToken();
const res = await fetch('/api/endpoint', {
  headers: { 'Authorization': `Bearer ${token}` }
});
```

The sign-in dialog (`AuthDialog.astro`) supports both email/password and Google OAuth. It's included in the layout — pages don't need to add it.

## Accessibility

Non-negotiable baseline for all new UI:

- **Skip link**: `<a href="#main-content">` is in Layout.astro.
- **Navigation**: `aria-current="page"` on the active nav link, `aria-haspopup` on dropdowns, `role="menu"` / `role="menuitem"` for dropdown items.
- **Decorative elements**: `aria-hidden="true"` on icons and decorative images.
- **Footer**: `role="contentinfo"`.
- **Forms**: every input gets a visible `<label>` or `aria-label`. Error messages are linked via `aria-describedby`.
- **Colour contrast**: use the design system tokens — they've been chosen for WCAG AA compliance in both light and dark modes.

## Where to put new code

| You want to add...                        | Goes in                                                              |
|-------------------------------------------|----------------------------------------------------------------------|
| A new page                                | `pages/[...lang]/my-page.astro` + translation JSON in `i18n/en/` and `i18n/cs/` |
| A reusable UI section                     | `components/MySection.astro` with a `Props` interface                |
| An icon                                   | `components/icons/MyIcon.astro` (inline SVG)                        |
| A shared translation string               | `i18n/{lang}/common.json`                                           |
| A page-specific translation string        | `i18n/{lang}/my-page.json`                                          |
| A new design token                        | `styles/global.css` under `@theme`                                  |
| A new global utility/helper               | `lib/my-helper.ts`                                                  |
| A new language                            | JSON files in `i18n/{lang}/` + entry in `locales` array in `utils.ts` |
| Admin-only page                           | `pages/[...lang]/admin/my-page.astro`                               |
