// @ts-check
import { execSync } from 'node:child_process';
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';
import tailwindcss from '@tailwindcss/vite';

const site = 'https://www.kalandra.tech';

/**
 * Returns the most recent git commit date across the given file paths.
 * Falls back to the current date if git history is unavailable.
 */
function getLastModified(...filePaths) {
  try {
    const out = execSync(
      `git log -1 --format=%cI -- ${filePaths.join(' ')}`,
      { encoding: 'utf-8' },
    ).trim();
    return out ? new Date(out) : new Date();
  } catch {
    return new Date();
  }
}

/**
 * Maps a sitemap URL to the source files that contribute to that page,
 * then returns the most recent git commit date across all of them.
 *
 * Each page depends on its .astro template, its per-page translation JSONs
 * (both en and cs), the shared Layout, and common.json translations.
 */
function getPageLastmod(url) {
  const path = new URL(url).pathname;
  // Strip locale prefix and trailing slash to get the page slug
  const stripped = path.replace(/^\/cs\//, '/').replace(/\/$/, '') || '/';
  const slug = stripped === '/' ? 'home' : stripped.slice(1); // 'about', 'hire-me', etc.
  const astroFile = slug === 'home' ? 'index.astro' : `${slug}.astro`;

  const files = [
    `src/pages/[...lang]/${astroFile}`,
    `src/i18n/en/${slug}.json`,
    `src/i18n/cs/${slug}.json`,
    'src/layouts/Layout.astro',
    'src/i18n/en/common.json',
    'src/i18n/cs/common.json',
  ];

  return getLastModified(...files);
}

// https://astro.build/config
export default defineConfig({
  site,
  integrations: [
    sitemap({
      filter: (page) => !page.includes('/profile'),
      i18n: {
        defaultLocale: 'en',
        locales: { en: 'en', cs: 'cs' },
      },
      changefreq: 'monthly',
      serialize(item) {
        const path = new URL(item.url).pathname.replace(/\/$/, '') || '/';

        // Homepage gets highest priority
        if (path === '/' || path === '/cs') {
          item.priority = 1.0;
          item.changefreq = 'weekly';
        }
        // Main content pages
        else if (['/about', '/project', '/hire-me'].some(p => path.endsWith(p))) {
          item.priority = 0.8;
        }
        // Dynamic/list pages
        else if (path.includes('/job-offers')) {
          item.priority = 0.7;
          item.changefreq = 'weekly';
        }
        // Legal pages
        else {
          item.priority = 0.3;
        }

        item.lastmod = getPageLastmod(item.url);
        return item;
      },
    }),
  ],
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'cs'],
    routing: {
      prefixDefaultLocale: false,
    },
  },
  vite: {
    plugins: [tailwindcss()]
  }
});
