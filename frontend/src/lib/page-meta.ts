/** A page under `pages/[...lang]/` opts into the sitemap by exporting this as `pageMeta`. */
export interface PageMeta {
  /** ISO date (YYYY-MM-DD) of the last meaningful content change — bump when editing the page. */
  updatedDate: string;
}
