import type { APIContext } from "astro";
import type { PageMeta } from "../lib/page-meta";
import { publishedPosts } from "../blog/posts";
import { locales, localePath, type Locale } from "../i18n/utils";

// Every static page opts in by exporting `pageMeta`; blog posts contribute
// their own metadata. One mechanism, no auto-discovery — see docs/frontend.md.
const pageModules = import.meta.glob("./**/*.astro", { eager: true }) as Record<string, { pageMeta?: PageMeta }>;

interface SitemapEntry {
  /** Route path without locale prefix or leading slash; "" is the home page. */
  path: string;
  /** ISO date (YYYY-MM-DD). */
  lastmod: string;
  /** Locales this URL exists in — all for static pages, the declared languages for posts. */
  locales: Locale[];
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

    entries.push({ path: route, lastmod, locales });
  }

  for (const post of posts) {
    entries.push({ path: `blog/${post.slug}`, lastmod: post.lastModified, locales: post.locales });
  }

  return entries.sort((a, b) => a.path.localeCompare(b.path));
}

function urlNodes(site: string, entry: SitemapEntry): string {
  const location = (locale: Locale) => `${site}${localePath(locale, entry.path === "" ? "home" : entry.path)}`;

  // hreflang needs a pair — single-language entries emit a bare <url>.
  const alternates =
    entry.locales.length > 1
      ? [
          ...entry.locales.map((locale) => `    <xhtml:link rel="alternate" hreflang="${locale}" href="${location(locale)}"/>`),
          `    <xhtml:link rel="alternate" hreflang="x-default" href="${location(entry.locales[0])}"/>`,
        ].join("\n")
      : undefined;

  return entry.locales
    .map((locale) =>
      [`  <url>`, `    <loc>${location(locale)}</loc>`, `    <lastmod>${entry.lastmod}</lastmod>`, alternates, `  </url>`]
        .filter((line) => line !== undefined)
        .join("\n"),
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
