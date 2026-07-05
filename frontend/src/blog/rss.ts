import rss from "@astrojs/rss";
import type { APIContext } from "astro";
import { localePath, type Locale } from "../i18n/utils";
import { publishedPosts } from "./posts";
import { feeds } from "./feeds";
import enBlog from "../i18n/en/blog.json";
import csBlog from "../i18n/cs/blog.json";

const descriptions: Record<Locale, string> = {
  en: enBlog.meta.description,
  cs: csBlog.meta.description,
};

// Summary-only by design — descriptions drive readers to the site instead of
// mirroring full posts.
export function localeFeed(context: APIContext, locale: Locale) {
  const posts = publishedPosts().filter((post) => post.locales.includes(locale));
  // ISO date strings order lexicographically, so .sort() finds the newest edit.
  const newestChange = posts
    .map((post) => post.lastModified)
    .sort()
    .at(-1);

  return rss({
    title: feeds[locale].title,
    description: descriptions[locale],
    site: context.site!,
    trailingSlash: false,
    xmlns: { atom: "http://www.w3.org/2005/Atom" },
    customData: [
      `<language>${locale}</language>`,
      `<atom:link href="${new URL(feeds[locale].path.slice(1), context.site)}" rel="self" type="application/rss+xml"/>`,
      ...(newestChange ? [`<lastBuildDate>${new Date(newestChange).toUTCString()}</lastBuildDate>`] : []),
    ].join(""),
    items: posts.map((post) => {
      const variant = post.metadata.variants[locale]!;
      return {
        title: variant.title,
        description: variant.summary,
        link: localePath(locale, `blog/${post.slug}`),
        pubDate: post.pubDate,
        categories: post.metadata.tags,
      };
    }),
  });
}
