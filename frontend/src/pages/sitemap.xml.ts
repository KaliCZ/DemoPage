import type { APIContext } from "astro";
import type { PageMeta } from "../lib/page-meta";
import { publishedPosts } from "../blog/posts";

// Every static page opts in by exporting `pageMeta`; blog posts contribute
// their own metadata. One mechanism, no auto-discovery — see docs/frontend.md.
const pageModules = import.meta.glob("./**/*.astro", { eager: true }) as Record<string, { pageMeta?: PageMeta }>;

interface SitemapEntry {
  /** Route path without locale prefix or leading slash; "" is the home page. */
  path: string;
  /** ISO date (YYYY-MM-DD). */
  lastmod: string;
}

const LOCALIZED_PAGES_PREFIX = "./[...lang]/";

function collectEntries(): SitemapEntry[] {
  const posts = publishedPosts();
  const entries: SitemapEntry[] = [];

  for (const [path, module] of Object.entries(pageModules)) {
    if (!module.pageMeta || !path.startsWith(LOCALIZED_PAGES_PREFIX)) continue;

    let route = path.slice(LOCALIZED_PAGES_PREFIX.length).replace(/\.astro$/, "");
    if (route === "index") route = "";
    // Normalize nested index pages ("blog/index" → "blog") BEFORE the blog-post
    // skip below, or the blog index itself would be dropped from the sitemap.
    if (route.endsWith("/index")) route = route.slice(0, -"/index".length);
    if (route.startsWith("blog/")) continue; // posts are added from their own metadata below

    // The blog index changes whenever a post lands — its lastmod tracks the newest post.
    const lastmod =
      route === "blog"
        ? [module.pageMeta.updatedDate, ...posts.map((post) => post.lastModified)].sort().at(-1)!
        : module.pageMeta.updatedDate;

    entries.push({ path: route, lastmod });
  }

  for (const post of posts) {
    entries.push({ path: `blog/${post.slug}`, lastmod: post.lastModified });
  }

  return entries.sort((a, b) => a.path.localeCompare(b.path));
}

function urlNodes(site: string, entry: SitemapEntry): string {
  const enLocation = entry.path === "" ? `${site}/` : `${site}/${entry.path}`;
  const csLocation = entry.path === "" ? `${site}/cs/` : `${site}/cs/${entry.path}`;

  const alternates = [
    `    <xhtml:link rel="alternate" hreflang="en" href="${enLocation}"/>`,
    `    <xhtml:link rel="alternate" hreflang="cs" href="${csLocation}"/>`,
    `    <xhtml:link rel="alternate" hreflang="x-default" href="${enLocation}"/>`,
  ].join("\n");

  return [enLocation, csLocation]
    .map((location) =>
      [`  <url>`, `    <loc>${location}</loc>`, `    <lastmod>${entry.lastmod}</lastmod>`, alternates, `  </url>`].join("\n"),
    )
    .join("\n");
}

export function GET(context: APIContext) {
  const site = (context.site?.toString() ?? "https://www.kalandra.tech").replace(/\/$/, "");
  const body = collectEntries()
    .map((entry) => urlNodes(site, entry))
    .join("\n");

  const xml = [
    `<?xml version="1.0" encoding="UTF-8"?>`,
    `<?xml-stylesheet type="text/xsl" href="/sitemap.xsl"?>`,
    `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">`,
    body,
    `</urlset>`,
    ``,
  ].join("\n");

  return new Response(xml, { headers: { "Content-Type": "application/xml; charset=utf-8" } });
}
