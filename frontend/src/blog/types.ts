import { defaultLocale, locales, type Locale } from "../i18n/utils";

export interface BlogPostVariant {
  title: string;
  /** Plain-text summary shown on the index, in the RSS feed (summary-only by design), and as meta description. */
  summary: string;
}

/**
 * A post is English-only, Czech-only, or bilingual; routes, index entries, feed
 * items, and sitemap URLs exist only for the declared languages. Declare a
 * language only when title, summary, AND the body are written in it — no
 * half-translations.
 */
export type BlogPostVariants = { en: BlogPostVariant; cs?: BlogPostVariant } | { en?: BlogPostVariant; cs: BlogPostVariant };

/** Metadata contract every blog post page exports — drives the index, RSS feeds, and sitemap. */
export interface BlogPostMetadata {
  variants: BlogPostVariants;
  /** ISO date (YYYY-MM-DD). */
  pubDate: string;
  /** ISO date (YYYY-MM-DD) — set on meaningful edits; drives the sitemap lastmod. */
  updatedDate?: string;
  tags: string[];
  /** Drafts stay in git but are never built: no page, no index entry, no RSS item, no sitemap entry. */
  draft?: boolean;
}

/** Declared languages in site order (en first). */
export function postLocales(metadata: BlogPostMetadata): Locale[] {
  return locales.filter((locale) => metadata.variants[locale] !== undefined);
}

/** getStaticPaths body for post pages — only declared languages get a route; a draft emits none. */
export function blogPostStaticPaths(metadata: BlogPostMetadata) {
  if (metadata.draft) return [];
  return postLocales(metadata).map((lang) => ({
    params: { lang: lang === defaultLocale ? undefined : lang },
  }));
}
