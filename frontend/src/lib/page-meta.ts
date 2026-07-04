/**
 * Sitemap contract for static pages. A page under `pages/[...lang]/` opts into
 * the sitemap by exporting `pageMeta`; pages without it (profile, admin, auth
 * callback) stay out. Blog posts are covered by their own post metadata.
 */
export interface PageMeta {
  /** ISO date (YYYY-MM-DD) of the last meaningful content change — bump when editing the page. */
  updatedDate: string;
}
