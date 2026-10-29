import { defaultLocale, locales, type Locale } from "../i18n/utils";

export interface BlogPostVariant {
  title: string;
  /** Plain text: it's also the RSS item body and the meta description. */
  summary: string;
}

/** Declare a language only when its title, summary and body are all written in it; routes, feed and sitemap follow the declared ones. */
export type BlogPostVariants = { en: BlogPostVariant; cs?: BlogPostVariant } | { en?: BlogPostVariant; cs: BlogPostVariant };

/** Every blog post page must export this as `metadata`. */
export interface BlogPostMetadata {
  variants: BlogPostVariants;
  /** ISO date (YYYY-MM-DD). */
  pubDate: string;
  /** ISO date (YYYY-MM-DD) of the last meaningful edit. */
  updatedDate?: string;
  tags: string[];
  /** Drafts stay in git but are never built or listed anywhere. */
  draft?: boolean;
}

/** Declared languages in site order (en first). */
export function postLocales(metadata: BlogPostMetadata): Locale[] {
  return locales.filter((locale) => metadata.variants[locale] !== undefined);
}

export function blogPostStaticPaths(metadata: BlogPostMetadata) {
  if (metadata.draft) return [];
  return postLocales(metadata).map((lang) => ({
    params: { lang: lang === defaultLocale ? undefined : lang },
  }));
}
