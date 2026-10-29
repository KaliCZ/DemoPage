# Blog

## Purpose

Technical articles in English, Czech, or both, with an RSS feed, reactions, view counts, and
threaded comments ([requirements](../product-requirements.md#blog)).

## How it works

### Posts are pages

Posts are first-class Astro pages, not a CMS — git is the source of truth.

- One `.astro` file per post under `pages/[...lang]/blog/<slug>.astro`; the filename is the slug.
- Every post exports a `metadata` constant (contract in `src/blog/types.ts`) and
  `export const getStaticPaths = () => blogPostStaticPaths(metadata);`. That metadata drives the
  index page, the RSS feed, and the sitemap.
- **A post declares its languages** through `variants` — English-only, Czech-only, or both. Each
  declared language gets its own title, summary, and route (`/blog/<slug>`, `/cs/blog/<slug>`);
  an undeclared language gets nothing (no route, feed item, sitemap URL, or hreflang claim).
- Bilingual bodies live in the same post file, switched with
  `lang === "cs" ? <Fragment>…</Fragment> : <Fragment>…</Fragment>`; code snippets stay shared,
  with English code comments.
- `draft: true` keeps a post in git but out of the build entirely.
- Posts wrap their content in `BlogPostLayout` and use `<BlogCode>` for snippets. Its syntax theme
  (`src/lib/code-theme.ts`) uses `--code-*` variables that `theme.css` maps to design tokens, so
  code blocks follow the palette and dark mode; posts never pass their own theme. The layout
  narrows hreflang to the declared languages and points the language picker at the translated
  post, or at the blog index when there is no translation.
- The index lists every post in both locales; a post without a variant in the current language
  shows in its own language with a language chip and links across.
- One feed at `/rss.xml` (identity in `src/blog/feeds.ts`), summary-only, advertised through
  `<link rel="alternate">` on every page. RSS has no per-item language, so each item title is
  prefixed with its language tags (`[EN]`, `[CS]`, or `[EN]/[CS]`) and a bilingual post shows
  both titles, default locale first. The item links to the default-locale page and carries the
  default-locale summary.

### Engagement

The `Kalandra.Blog` domain stores reactions, views, and comments per post.

- **Post catalog** — `BlogPostCatalog` is the backend's list of published slugs, each with a
  hand-assigned comment-stream id; a slug the catalog doesn't know gets `404` from every blog
  endpoint. All language variants of a post share one discussion.
- **Visitor id** — the browser mints a random id, keeps it in `localStorage`
  (`src/lib/visitor.ts`), and sends it as `X-Visitor-Id`. Views and reactions are keyed by the
  user id when signed in and by the visitor id otherwise. On sign-in the client links the visitor
  id to the account once, moving its anonymous views and reactions over. A visitor id already
  linked to another account gets `409`, and the client mints a fresh one.
- **Reactions** — one row per reactor and kind; toggling adds or removes it.
- **Comments** — one append-only event stream per post. Deleting is a soft delete by the author or
  the admin, and nobody can reply to a deleted comment. The index's comment counts come from an
  in-memory cache, so the index doesn't replay every stream per request.
- New comments trigger [notification emails](notifications.md).

## Decisions

- **Posts in git, not a CMS** — a post gets code review, typed metadata, and the same build and
  tests as the rest of the site.
- **One feed, not one per language** — for a mostly-English personal blog, a feed the reader
  subscribes to once beats making them pick a language edition.
- **Anonymous engagement** — reactions and views work without an account, so the visitor id makes
  them countable and lets them follow the visitor into their account later.

## Invariants

- A language is declared only when its title, summary, and body are all written — no
  half-translations.
- Publishing a post also adds it to `BlogPostCatalog` with a new hand-assigned stream id; an E2E
  test reacts on every post and fails when the two lists drift.
- A post's stream id never changes once it has comments, even if its slug is renamed.
