import type { Locale } from "../i18n/utils";
import { postLocales, type BlogPostMetadata, type BlogPostVariant } from "./types";

export interface BlogPost {
  slug: string;
  metadata: BlogPostMetadata;
  /** Declared languages in site order (en first). */
  locales: Locale[];
  pubDate: Date;
  updatedDate?: Date;
  /** Sitemap lastmod source: updatedDate when set, else pubDate. */
  lastModified: string;
}

// `**` sidesteps glob character-class parsing of the literal [...lang] directory;
// the index page is excluded here so this module and the index don't import each other.
const modules = import.meta.glob(["../pages/**/blog/*.astro", "!../pages/**/blog/index.astro"], {
  eager: true,
}) as Record<string, { metadata?: BlogPostMetadata }>;

function parseIsoDate(value: string, source: string): Date {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) throw new Error(`Invalid date "${value}" in blog post metadata (${source})`);
  return date;
}

/** All published posts, newest first. Drafts are excluded. */
export function publishedPosts(): BlogPost[] {
  return Object.entries(modules)
    .map(([path, module]) => {
      if (!module.metadata) throw new Error(`Blog post ${path} does not export a metadata constant`);
      const slug = path
        .replace(/\.astro$/, "")
        .split("/")
        .pop()!;
      const metadata = module.metadata;
      return {
        slug,
        metadata,
        locales: postLocales(metadata),
        pubDate: parseIsoDate(metadata.pubDate, path),
        updatedDate: metadata.updatedDate ? parseIsoDate(metadata.updatedDate, path) : undefined,
        lastModified: metadata.updatedDate ?? metadata.pubDate,
      };
    })
    .filter((post) => !post.metadata.draft)
    .sort((a, b) => b.pubDate.getTime() - a.pubDate.getTime());
}

/** The `lang` variant when declared, else the post's first language — `locale` tells callers when they link across languages. */
export function resolveVariant(post: BlogPost, lang: Locale): { locale: Locale; variant: BlogPostVariant } {
  const locale = post.locales.includes(lang) ? lang : post.locales[0];
  return { locale, variant: post.metadata.variants[locale]! };
}
