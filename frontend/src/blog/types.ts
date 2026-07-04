import { defaultLocale, locales } from "../i18n/utils";

/** Metadata contract every blog post page exports — drives the index, RSS feed, and sitemap. */
export interface BlogPostMetadata {
  title: string;
  /** Plain-text summary shown on the index, in the RSS feed (summary-only by design), and as meta description. */
  summary: string;
  /** ISO date (YYYY-MM-DD). */
  pubDate: string;
  /** ISO date (YYYY-MM-DD) — set on meaningful edits; drives the sitemap lastmod. */
  updatedDate?: string;
  tags: string[];
  /** Drafts stay in git but are never built: no page, no index entry, no RSS item, no sitemap entry. */
  draft?: boolean;
}

/** getStaticPaths body for post pages — a draft emits no paths, so it is never built or publicly reachable. */
export function blogPostStaticPaths(metadata: BlogPostMetadata) {
  if (metadata.draft) return [];
  return locales.map((lang) => ({
    params: { lang: lang === defaultLocale ? undefined : lang },
  }));
}
