# Frontend Guide

Astro SSG site with Tailwind CSS, deployed to Cloudflare Pages as static files. All interactive behaviour is vanilla JS in `<script>` tags — no React, no Vue, no client-side framework.

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
- **`global.css`** — design tokens (`--color-*`, `--font-*`), base element resets, and dark mode overrides. This is the design system, not a dumping ground.
- **Scoped `<style>` in components** — for component-specific styles that can't be expressed as utilities (complex selectors, animations, pseudo-elements).

Avoid a separate CSS file per component. Astro's scoped `<style>` handles isolation.

## Dark mode

Uses `.dark` class toggled on `<html>`. CSS custom properties in `global.css` are overridden under `.dark`. Flash prevention: `Layout.astro` applies `.no-transitions` on load and removes it after a frame.

All new UI must work in both modes. Use the semantic colour tokens (`bg-background`, `text-on-surface`, etc.) rather than hardcoded colours.

## Client-side JavaScript

All interactivity is vanilla JS in `<script>` tags — no framework.

- `<script>` tags go at the bottom of the page or component file, after the markup.
- Prefer `data-*` attributes for JS hooks over class names or IDs. This separates styling from behaviour.
- Keep scripts focused — one concern per script block. If a page needs multiple independent behaviours, use separate `<script>` tags.

## Auth on the frontend

Supabase Auth with email/password + Google OAuth. Client-side auth state is managed via `@supabase/supabase-js`.

`Layout.astro` exposes three globals for pages and scripts:
- `window.__supabase` — the Supabase client instance.
- `window.__getAccessToken()` — returns the current JWT (for API calls).
- `window.__getUser()` — returns the current user object.

The sign-in dialog (`AuthDialog.astro`) is included in the layout — pages don't need to add it.

## Accessibility

Non-negotiable baseline for all new UI:

- **Skip link**: `<a href="#main-content">` is in Layout.astro.
- **Navigation**: `aria-current="page"` on the active nav link, `aria-haspopup` on dropdowns, `role="menu"` / `role="menuitem"` for dropdown items.
- **Decorative elements**: `aria-hidden="true"` on icons and decorative images.
- **Forms**: every input gets a visible `<label>` or `aria-label`. Error messages are linked via `aria-describedby`.
- **Colour contrast**: use the design system tokens — they've been chosen for WCAG AA compliance in both light and dark modes.
