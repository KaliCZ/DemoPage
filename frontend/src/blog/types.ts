import type { Locale } from "../i18n/utils";

/**
 * Metadata each blog post .astro file exports as `post`. Defines every
 * field the listing page, post chrome, and the RSS feed need to render —
 * the post body itself is owned by the .astro file's template.
 */
export interface BlogPostMeta {
  /** URL slug — must match the .astro file name and the API slug grammar `[a-z0-9][a-z0-9-]{0,79}`. */
  slug: string;
  /** ISO 8601 publication timestamp. Used for sort order, RSS pubDate, and the human-readable date. */
  pubDate: string;
  /** ISO 8601 last-edit timestamp; defaults to pubDate when absent. */
  updatedDate?: string;
  /** Per-locale post title. */
  title: Record<Locale, string>;
  /** Per-locale plaintext summary used by the listing card and RSS description. */
  summary: Record<Locale, string>;
  /** Topical tags used by the listing chip row. */
  tags: string[];
  /** When true, the post is excluded from the index, RSS feed, and sitemap. */
  draft?: boolean;
  /** Optional canonical link for posts also published elsewhere. */
  canonicalUrl?: string;
}
