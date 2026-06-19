// @ts-check
import { execSync } from "node:child_process";
import { defineConfig } from "astro/config";
import sitemap from "@astrojs/sitemap";
import icon from "astro-icon";
import tailwindcss from "@tailwindcss/vite";
import { sentryVitePlugin } from "@sentry/vite-plugin";

const site = "https://www.kalandra.tech";

/**
 * Returns the most recent git commit date across the given file paths.
 * Falls back to the current date if git history is unavailable.
 */
function getLastModified(...filePaths) {
  try {
    const out = execSync(`git log -1 --format=%cI -- ${filePaths.join(" ")}`, {
      encoding: "utf-8",
    }).trim();
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
  const stripped = path.replace(/^\/cs\//, "/").replace(/\/$/, "") || "/";
  const slug = stripped === "/" ? "home" : stripped.slice(1); // 'about', 'hire-me', etc.
  const astroFile = slug === "home" ? "index.astro" : `${slug}.astro`;

  const files = [
    `src/pages/[...lang]/${astroFile}`,
    `src/i18n/en/${slug}.json`,
    `src/i18n/cs/${slug}.json`,
    "src/layouts/Layout.astro",
    "src/i18n/en/common.json",
    "src/i18n/cs/common.json",
  ];

  return getLastModified(...files);
}

// Aspire injects PORT and services__api__http__0; both are undefined outside Aspire.
const aspireApiUrl = process.env.services__api__http__0;

// Source map upload to Sentry only runs when SENTRY_AUTH_TOKEN is present (set in the prod
// frontend-deploy job). Locally and in CI test builds the plugin is omitted entirely, so the
// build stays quiet and offline.
const sentryAuthToken = process.env.SENTRY_AUTH_TOKEN;
const sentryOrg = process.env.SENTRY_ORG;
const sentryProject = process.env.SENTRY_PROJECT;

export default defineConfig({
  site,
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : undefined,
  },
  build: {
    inlineStylesheets: "always",
    // 'hidden' emits .map files but strips the sourceMappingURL comment from the bundle, so the
    // browser doesn't request them. Sentry resolves stack traces via the uploaded copies instead.
    // Only meaningful when the Sentry upload plugin is active — otherwise the .map files would be
    // generated and thrown away. Keep `false` in dev/CI to skip the cost.
    sourcemap: sentryAuthToken ? "hidden" : false,
  },
  integrations: [
    icon(),
    sitemap({
      xslURL: "/sitemap.xsl",
      filter: (page) => !page.includes("/profile") && !page.includes("/admin"),
      i18n: {
        defaultLocale: "en",
        locales: { en: "en", cs: "cs" },
      },
      serialize(item) {
        item.lastmod = getPageLastmod(item.url);
        return item;
      },
    }),
  ],
  i18n: {
    defaultLocale: "en",
    locales: ["en", "cs"],
    routing: {
      prefixDefaultLocale: false,
    },
  },
  vite: {
    plugins: [
      tailwindcss(),
      ...(sentryAuthToken && sentryOrg && sentryProject
        ? [
            sentryVitePlugin({
              authToken: sentryAuthToken,
              org: sentryOrg,
              project: sentryProject,
              sourcemaps: {
                // Astro emits the browser bundle into dist/_astro; upload only those, plus the
                // .map files alongside. Skip the static HTML / public assets.
                filesToDeleteAfterUpload: ["dist/**/*.map"],
              },
              // Tie uploaded maps to the deployed commit so Sentry can correlate stack traces
              // across releases without us having to invent a version string.
              release: { name: process.env.GITHUB_SHA },
            }),
          ]
        : []),
    ],
    server: {
      strictPort: !!process.env.VITE_STRICT_PORT,
      proxy: {
        "/api": aspireApiUrl ?? "http://localhost:5000",
        "/health": aspireApiUrl ?? "http://localhost:5000",
      },
    },
    // Pre-bundle deps that are only reached via dynamic import — avoids a mid-load re-optimize that 504s in-flight requests.
    optimizeDeps: {
      include: [
        "@supabase/supabase-js",
        "@opentelemetry/api",
        "@opentelemetry/context-zone",
        "@opentelemetry/exporter-trace-otlp-http",
        "@opentelemetry/instrumentation",
        "@opentelemetry/instrumentation-document-load",
        "@opentelemetry/instrumentation-fetch",
        "@opentelemetry/resources",
        "@opentelemetry/sdk-trace-web",
        "@opentelemetry/semantic-conventions",
        "@sentry/browser",
      ],
    },
  },
});
