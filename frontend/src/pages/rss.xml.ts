import rss from "@astrojs/rss";
import type { APIContext } from "astro";
import { publishedPosts } from "../blog/posts";
import enBlog from "../i18n/en/blog.json";

// One site-wide feed with canonical (English) URLs. Summary-only by design —
// the description drives readers to the site instead of mirroring full posts.
export function GET(context: APIContext) {
  const posts = publishedPosts();
  // ISO date strings order lexicographically, so .sort() finds the newest edit.
  const newestChange = posts
    .map((post) => post.lastModified)
    .sort()
    .at(-1);

  return rss({
    title: "kalandra.tech — Blog",
    description: enBlog.meta.description,
    site: context.site!,
    trailingSlash: false,
    xmlns: { atom: "http://www.w3.org/2005/Atom" },
    customData: [
      `<language>en</language>`,
      `<atom:link href="${new URL("rss.xml", context.site)}" rel="self" type="application/rss+xml"/>`,
      ...(newestChange ? [`<lastBuildDate>${new Date(newestChange).toUTCString()}</lastBuildDate>`] : []),
    ].join(""),
    items: posts.map((post) => ({
      title: post.metadata.title,
      description: post.metadata.summary,
      link: `/blog/${post.slug}`,
      pubDate: post.pubDate,
      categories: post.metadata.tags,
    })),
  });
}
