# Frontend Conventions

How to write frontend code here. How the frontend is built and why — routing, i18n, the two
client-side tiers, the sitemap — is in [architecture/frontend.md](../architecture/frontend.md);
blog posts are in [architecture/blog.md](../architecture/blog.md).

## Translations

- One JSON file per page per language under `src/i18n/<lang>/`, plus `common.json` for shared
  strings (nav, footer, auth, a11y).
- Write raw UTF-8 characters in JSON, never Unicode escapes (`č`, not `č`).

## Keeping pages small: extract components

**The single most important rule for frontend code quality.** Astro page files stay focused on
routing, data loading, and page-level layout.

**When to extract:**

- A page file exceeds ~300 lines.
- A distinct visual section has its own heading, layout, and internal structure (a detail card, a
  form, a diagram).
- The same markup appears on multiple pages.
- A section has enough props or logic to benefit from its own `interface Props`.

**How to extract:**

1. Create `src/components/MySection.astro`.
2. Define a `Props` interface in the frontmatter for everything the component needs
   (translations, data, flags).
3. Move the markup and any scoped `<style>` into the component.
4. Import and use it in the page.

**What stays in the page file:** `getStaticPaths()` and locale resolution, top-level data
fetching and page-level variables, the page's layout skeleton, and inline `<script>` tags that
wire up page-level interactivity.

**What to avoid:** components that are thin wrappers around a single element, and passing a whole
translation object when the component needs a few strings — pass a focused subset.

## Client-side JavaScript

- Pick the tier by what the code does — see
  [architecture/frontend.md](../architecture/frontend.md#how-it-works).
- `<script>` tags go at the bottom of the page or component file, one concern per block, with
  `data-*` attributes as JS hooks.
- Vue islands load with `client:idle` and take translations and config as props — a focused
  subset, not whole translation files.
- Auth plumbing is the same in both tiers: the `window.__*` globals and the `auth-change` event,
  with typed wrappers in `lib/auth.ts` for module code.

## Styling

- **Utility classes in markup** — the default.
- **`theme.css`** — the design system: every `--color-*` and `--font-*` token, light values plus
  the `.dark` overrides. All app colors resolve from here — never hardcode a color elsewhere.
- **`global.css`** — base element resets, font loading, and shared component styling that consumes
  the tokens. Not a dumping ground.
- **Scoped `<style>` in components** — for what utilities can't express (complex selectors,
  animations, pseudo-elements). No separate CSS file per component.

### Visual language

- Three fonts with fixed roles: Manrope for headlines, Inter for body text, Space Grotesk for
  labels, dates, tags, and code labels.
- Depth comes from stacking surface tokens (`surface`, `surface-container-low` … `-highest`), not
  from drop shadows.
- Section boundaries come from background shifts; where a container needs an edge, use a faint
  `outline-variant` border (10–40% opacity), never a solid one.
- Generous whitespace between major sections; when a screen feels busy, add space before adding
  lines.

## Dark mode

A `.dark` class on `<html>` switches the tokens in `theme.css`. `Layout.astro` applies
`.no-transitions` on load and removes it after a frame, so the theme swap doesn't animate. All new
UI must work in both modes through the semantic tokens (`bg-background`, `text-on-surface`, …).

## Sitemap

A page that should be indexed exports `pageMeta`; **bump its `updatedDate` when you meaningfully
edit the page**. Auth-gated pages don't export it and pass `noindex` to `Layout`.

## Accessibility

Non-negotiable baseline for all new UI:

- **Skip link**: `<a href="#main-content">` is in `Layout.astro`.
- **Navigation**: `aria-current="page"` on the active nav link, `aria-haspopup` on dropdowns,
  `role="menu"` / `role="menuitem"` for dropdown items.
- **Decorative elements**: `aria-hidden="true"` on icons and decorative images.
- **Forms**: every input has a visible `<label>` or an `aria-label`; error messages are linked
  through `aria-describedby`.
- **Color contrast**: use the design-system tokens — they are chosen for WCAG AA in both modes.

## Tests

- The specs directly under `tests/` run against the built static site with no backend: rendering,
  navigation, i18n, dark mode, and the sign-in dialog.
- `tests/e2e/` drives the full stack — real auth, API, and database — with users each test signs up itself.
