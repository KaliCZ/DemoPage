import rss from "@astrojs/rss";
import type { APIContext } from "astro";
import { defaultLocale, localePath } from "../i18n/utils";
import { publishedPosts } from "../blog/posts";
import { feed } from "../blog/feeds";
import enBlog from "../i18n/en/blog.json";

// One feed for the whole blog. A post is written in English, Czech, or both;
// RSS has no per-item language field, so each item is prefixed with its
// language tags ("[EN]/[CS]") and a bilingual post shows both titles
// ("English / Czech"), keeping the language visible in any reader. The item
// links to the post's default-locale page, which carries the language
// switcher. Summary-only by design. The channel language is the site default.
export function GET(context: APIContext) {
  const posts = publishedPosts();
  // ISO date strings order lexicographically, so .sort() finds the newest edit.
  const newestChange = posts
    .map((post) => post.lastModified)
    .sort()
    .at(-1);

  return rss({
    title: feed.title,
    description: enBlog.meta.description,
    site: context.site!,
    trailingSlash: false,
    xmlns: { atom: "http://www.w3.org/2005/Atom" },
    customData: [
      `<language>${defaultLocale}</language>`,
      `<atom:link href="${new URL(feed.path.slice(1), context.site)}" rel="self" type="application/rss+xml"/>`,
      ...(newestChange ? [`<lastBuildDate>${new Date(newestChange).toUTCString()}</lastBuildDate>`] : []),
    ].join(""),
    items: posts.map((post) => {
      const primary = post.locales.includes(defaultLocale) ? defaultLocale : post.locales[0];
      const tags = post.locales.map((locale) => `[${locale.toUpperCase()}]`).join("/");
      const titles = post.locales.map((locale) => post.metadata.variants[locale]!.title).join(" / ");
      return {
        title: `${tags} ${titles}`,
        description: post.metadata.variants[primary]!.summary,
        link: localePath(primary, `blog/${post.slug}`),
        pubDate: post.pubDate,
        categories: post.metadata.tags,
      };
    }),
  });
}
