/**
 * Public view/reader counts stay hidden until the pre-rollout Cloudflare traffic is
 * seeded, so the first rollout doesn't show a misleading near-zero. The follow-up PR
 * flips this on (and fixes the reader dedup + removes the seeding time parameter).
 */
export const SHOW_PUBLIC_STATS = false;
