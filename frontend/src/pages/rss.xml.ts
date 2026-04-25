import type { APIRoute } from "astro";
import { allPosts } from "../blog/posts";
import type { BlogPostMeta } from "../blog/types";

// Hand-rolled RSS 2.0 feed — the standard isn't large enough to justify a
// dependency, and Astro's static endpoints make this a one-file build step.
// Summary-only on purpose (description, not content:encoded): the goal is to
// drive readers to the site, not to ship the whole article in the feed.

export const GET: APIRoute = ({ site }) => {
  if (!site) throw new Error("astro.config `site` must be set for RSS feed generation.");
  const xml = buildRssXml(site, allPosts);
  return new Response(xml, {
    headers: {
      "Content-Type": "application/rss+xml; charset=utf-8",
      "Cache-Control": "public, max-age=600",
    },
  });
};

function buildRssXml(site: URL, posts: BlogPostMeta[]): string {
  const siteHref = trimTrailingSlash(site.href);
  const feedUrl = `${siteHref}/rss.xml`;
  const lastBuildDate = posts.length > 0 ? new Date(posts[0].pubDate) : new Date();

  const items = posts.map((post) => buildItem(siteHref, post)).join("\n");

  return `<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">
  <channel>
    <title>${escapeXml("kalandra.tech blog")}</title>
    <link>${escapeXml(siteHref + "/blog")}</link>
    <description>${escapeXml("Posts on engineering, leadership, and tools I'm building.")}</description>
    <language>en-us</language>
    <lastBuildDate>${toRfc822(lastBuildDate)}</lastBuildDate>
    <atom:link href="${escapeXml(feedUrl)}" rel="self" type="application/rss+xml" />
${items}
  </channel>
</rss>
`;
}

function buildItem(siteHref: string, post: BlogPostMeta): string {
  const link = `${siteHref}/blog/${post.slug}`;
  const guid = post.canonicalUrl ?? link;
  // English title and summary for the feed; per-locale variants stay in the
  // post's metadata for the on-site rendering. RSS readers don't have a clean
  // way to express alternate-language items, so we ship the canonical English.
  const title = post.title.en;
  const description = post.summary.en;

  const categories = post.tags.map((tag) => `      <category>${escapeXml(tag)}</category>`).join("\n");

  return `    <item>
      <title>${escapeXml(title)}</title>
      <link>${escapeXml(link)}</link>
      <guid isPermaLink="${post.canonicalUrl ? "false" : "true"}">${escapeXml(guid)}</guid>
      <pubDate>${toRfc822(new Date(post.pubDate))}</pubDate>
      <description>${escapeXml(description)}</description>
${categories}
    </item>`;
}

function trimTrailingSlash(s: string) {
  return s.endsWith("/") ? s.slice(0, -1) : s;
}

function escapeXml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&apos;");
}

function toRfc822(date: Date): string {
  // RFC 822 is what the RSS 2.0 spec mandates for pubDate / lastBuildDate.
  // toUTCString() is already RFC 7231-compliant, which is a strict subset of
  // RFC 822 (the only meaningful difference is the use of "GMT" instead of
  // "+0000", and that's accepted everywhere).
  return date.toUTCString();
}
