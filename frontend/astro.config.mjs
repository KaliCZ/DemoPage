// @ts-check
import { execSync } from "node:child_process";
import { defineConfig } from "astro/config";
import sitemap from "@astrojs/sitemap";
import icon from "astro-icon";
import tailwindcss from "@tailwindcss/vite";

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

// https://astro.build/config
// When launched under Aspire, AppHost injects PORT (the allocated frontend
// port) and services__api__http__0 (the API's discovery URL). Outside Aspire
// both are undefined and Astro/Vite fall back to their normal defaults.
const aspireApiUrl = process.env.services__api__http__0;

export default defineConfig({
  site,
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : undefined,
  },
  build: {
    inlineStylesheets: "always",
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
    plugins: [tailwindcss()],
    server: {
      strictPort: !!process.env.VITE_STRICT_PORT,
      proxy: {
        "/api": aspireApiUrl ?? "http://localhost:5000",
        "/health": aspireApiUrl ?? "http://localhost:5000",
      },
    },
    // Pre-declare every dependency that's only reached via a dynamic
    // import (supabase.ts uses import("@supabase/supabase-js"); Layout
    // uses import("../lib/otel-dev")). Without this Vite discovers them
    // mid-page-load and triggers a re-optimize, which 504s any in-flight
    // dep request — including Astro's dev toolbar — until it finishes.
    // Pre-bundling them at cold start avoids the disruptive interrupt.
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
      ],
    },
  },
});
