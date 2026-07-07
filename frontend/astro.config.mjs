// @ts-check
import { defineConfig } from "astro/config";
import vue from "@astrojs/vue";
import icon from "astro-icon";
import tailwindcss from "@tailwindcss/vite";
import { sentryVitePlugin } from "@sentry/vite-plugin";

const site = "https://www.kalandra.tech";

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
  },
  // The sitemap is a custom endpoint (src/pages/sitemap.xml.ts) fed by per-page
  // metadata exports, not an integration.
  integrations: [icon(), vue()],
  i18n: {
    defaultLocale: "en",
    locales: ["en", "cs"],
    routing: {
      prefixDefaultLocale: false,
    },
  },
  vite: {
    build: {
      // 'hidden' emits .map files but strips the sourceMappingURL comment from the bundle, so the
      // browser never requests them; Sentry resolves stack traces via the uploaded copies (matched
      // by debug ID). Must live under `vite.build` — Astro's own `build` key ignores `sourcemap`.
      // Only meaningful when the upload plugin below is active; keep `false` in dev/CI to skip the cost.
      sourcemap: sentryAuthToken ? "hidden" : false,
    },
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
