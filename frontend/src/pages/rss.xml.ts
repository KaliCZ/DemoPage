import rss from "@astrojs/rss";
import type { APIContext } from "astro";
import { publishedPosts } from "../blog/posts";
import enBlog from "../i18n/en/blog.json";

// One site-wide feed with canonical (English) URLs. Summary-only by design —
// the description drives readers to the site instead of mirroring full posts.
export function GET(context: APIContext) {
  return rss({
    title: "kalandra.tech — Blog",
    description: enBlog.meta.description,
    site: context.site!,
    trailingSlash: false,
    items: publishedPosts().map((post) => ({
      title: post.metadata.title,
      description: post.metadata.summary,
      link: `/blog/${post.slug}`,
      pubDate: post.pubDate,
      categories: post.metadata.tags,
    })),
  });
}
