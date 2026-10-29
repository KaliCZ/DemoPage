// @ts-check
import { defineConfig } from "astro/config";
import vue from "@astrojs/vue";
import icon from "astro-icon";
import tailwindcss from "@tailwindcss/vite";
import { sentryVitePlugin } from "@sentry/vite-plugin";

const site = "https://www.kalandra.tech";

// Aspire injects PORT and services__api__http__0; both are undefined outside Aspire.
const aspireApiUrl = process.env.services__api__http__0;

// Only the production deploy sets SENTRY_AUTH_TOKEN; every other build skips the upload plugin and stays offline.
const sentryAuthToken = process.env.SENTRY_AUTH_TOKEN;
const sentryOrg = process.env.SENTRY_ORG;
const sentryProject = process.env.SENTRY_PROJECT;

export default defineConfig({
  site,
  // Paired with `build.format: "file"` so Cloudflare Pages serves the slash-less URLs that canonicals, sitemap, and links declare.
  trailingSlash: "never",
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : undefined,
  },
  build: {
    format: "file",
    inlineStylesheets: "always",
  },
  // No sitemap integration — src/pages/sitemap.xml.ts builds it from per-page metadata.
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
      // Must live under `vite.build` — Astro's own `build` ignores `sourcemap`; "hidden" keeps the sourceMappingURL out of the bundle.
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
                // Deleted after upload so Cloudflare Pages never serves them.
                filesToDeleteAfterUpload: ["dist/**/*.map"],
              },
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
        "@opentelemetry/core",
        "@opentelemetry/exporter-trace-otlp-http",
        "@opentelemetry/instrumentation",
        "@opentelemetry/instrumentation-document-load",
        "@opentelemetry/instrumentation-fetch",
        "@opentelemetry/resources",
        "@opentelemetry/sdk-trace",
        "@opentelemetry/semantic-conventions",
        "@sentry/browser",
      ],
    },
  },
});
