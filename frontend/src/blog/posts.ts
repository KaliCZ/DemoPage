import type { BlogPostMeta } from "./types";

// Each post .astro file under src/pages/[...lang]/blog/ exports a `post`
// constant carrying its metadata. Importing them via `import.meta.glob`
// keeps the listing, RSS, and per-post chrome in sync with the files on
// disk — adding a post is just dropping in a new .astro file.
const modules = import.meta.glob<{ post?: BlogPostMeta }>("/src/pages/**/blog/*.astro", { eager: true });

export const allPosts: BlogPostMeta[] = Object.entries(modules)
  .filter(([path]) => !path.endsWith("/index.astro"))
  .map(([, m]) => m.post)
  .filter((p): p is BlogPostMeta => !!p && !p.draft)
  .sort((a, b) => +new Date(b.pubDate) - +new Date(a.pubDate));
