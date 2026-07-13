/** Build-time card data for the blog index island, precomputed for the page's language. */
export interface BlogListPost {
  slug: string;
  url: string;
  title: string;
  summary: string;
  tags: string[];
  pubDateIso: string;
  pubDateLabel: string;
  updatedDateIso?: string;
  updatedDateLabel?: string;
  /** Set when the post has no variant in the page language — the card links across. */
  crossLocale?: string;
  crossLocaleLabel?: string;
}

/** Wire shape of one GET /api/blog/stats entry (BlogPostStatsResponse on the backend). */
export interface BlogPostStats {
  slug: string;
  totalViews: number;
  uniqueVisitors: number;
  totalReactions: number;
  totalComments: number;
  /** null when the caller is anonymous. */
  viewerViews: number | null;
}

export interface BlogStatsResponse {
  posts: BlogPostStats[];
}
