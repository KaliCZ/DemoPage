// Dev-only OpenTelemetry initialization. Imported via a dynamic import
// gated on import.meta.env.DEV in Layout.astro, so Vite tree-shakes the
// whole module — and its ~100KB of @opentelemetry/* deps — out of the
// production bundle. In prod, BetterStack/Sentry owns observability.
//
// Emits to the Aspire dashboard's OTLP HTTP endpoint. The URL is injected
// by the AppHost via PUBLIC_OTLP_TRACES_ENDPOINT (it varies per AppHost
// instance because the OTLP port is dynamically reserved).

import type { Context } from "@opentelemetry/api";
import { DiagConsoleLogger, DiagLogLevel, diag, SpanKind, trace } from "@opentelemetry/api";
import { ZoneContextManager } from "@opentelemetry/context-zone";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { registerInstrumentations } from "@opentelemetry/instrumentation";
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchSpanProcessor, WebTracerProvider } from "@opentelemetry/sdk-trace-web";
import { ATTR_SERVICE_NAME } from "@opentelemetry/semantic-conventions";

const endpoint = import.meta.env.PUBLIC_OTLP_TRACES_ENDPOINT;

// Surface state on window so you can poke at it from devtools — `import.meta`
// can't be evaluated from the console, so this is the easy way to check.
(window as unknown as { __otelDev: unknown }).__otelDev = {
  envDev: import.meta.env.DEV,
  endpoint: endpoint ?? null,
  initialized: false,
};

if (!endpoint) {
  console.warn(
    "[otel-dev] PUBLIC_OTLP_TRACES_ENDPOINT is not set — frontend spans will not be emitted. Check `window.__otelDev` and verify the AppHost is injecting the env var.",
  );
} else {
  // Surface SDK internals — export attempts, exporter errors, CORS
  // rejections — straight to the browser console. Dev-only, so verbosity
  // is fine.
  diag.setLogger(new DiagConsoleLogger(), DiagLogLevel.INFO);

  // Context manager that falls back to the page span whenever nothing
  // else is active. Without this, fetches that happen outside an
  // explicit `context.with(pageContext, ...)` scope (most of them) start
  // their own root span and the trace gets named "HTTP GET ..." instead
  // of "page /...". By keeping pageContext as the implicit floor, every
  // fetch and every async continuation that lacks its own parent gets
  // pageSpan as its parent instead.
  class PageRootContextManager extends ZoneContextManager {
    private pageContext: Context | null = null;
    setPageContext(ctx: Context) {
      this.pageContext = ctx;
    }
    override active(): Context {
      const ctx = super.active();
      if (this.pageContext && !trace.getSpan(ctx)) {
        return this.pageContext;
      }
      return ctx;
    }
  }
  const contextManager = new PageRootContextManager();

  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: "web",
    }),
    spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: endpoint }))],
  });
  provider.register({
    contextManager,
  });

  registerInstrumentations({
    instrumentations: [
      new FetchInstrumentation({
        // Same-origin (Astro dev proxy) and our backend. Without this list
        // the auto-instrumentation only adds traceparent to same-origin
        // fetches, which is usually fine in dev since the Vite proxy keeps
        // /api/* on the page origin.
        propagateTraceHeaderCorsUrls: [/^https?:\/\/localhost(:\d+)?\//, /^https?:\/\/127\.0\.0\.1(:\d+)?\//],
        // Drop telemetry-of-telemetry: don't create spans for the OTLP
        // exporter's own POSTs (would self-loop) or for BetterStack /
        // Sentry ingestion. Endpoints that aren't application traffic
        // just add noise to the dashboard.
        ignoreUrls: [
          new RegExp("^" + (endpoint ?? "").replace(/[/.]/g, "\\$&")),
          /betterstackdata\.com\//,
          /betterstack\.net\//,
          /sentry\.io\//,
        ],
        // Default span name is "HTTP GET" plus status — useless for picking
        // out which endpoint took 800ms in a list. Rename to "<METHOD> <path>"
        // via requestHook (fires before the request goes out, so the name
        // is set before any other code path can read it). Handles Request,
        // URL, and plain-string inputs; query string is dropped.
        requestHook: (span, request) => {
          let rawUrl: string | null = null;
          let method = "GET";
          if (typeof request === "string") {
            rawUrl = request;
          } else if (request instanceof URL) {
            rawUrl = request.toString();
          } else if (request && typeof request === "object") {
            if ("url" in request && typeof (request as Request).url === "string") {
              rawUrl = (request as Request).url;
            }
            if ("method" in request && typeof (request as Request).method === "string") {
              method = (request as Request).method;
            }
          }
          if (rawUrl) {
            try {
              const url = new URL(rawUrl, location.href);
              span.updateName(`${method} ${url.pathname}`);
            } catch {
              /* fall back to the default name */
            }
          }
        },
      }),
    ],
  });

  // Start a deliberate page-view span and feed it to the context manager as
  // the implicit floor. Every fetch, every async continuation that doesn't
  // already have its own parent now lands under this span — so the trace
  // root is always "page /<pathname>" instead of whichever fetch happened
  // to fire first.
  const tracer = trace.getTracer("web");
  const pageSpan = tracer.startSpan(`page ${location.pathname}`, { kind: SpanKind.INTERNAL });
  contextManager.setPageContext(trace.setSpan(contextManager.active(), pageSpan));

  // End the page span as soon as the page finishes loading rather than at
  // pagehide. The trace root has to exist for the dashboard to render
  // anything, but BatchSpanProcessor doesn't export pageSpan until it
  // ends — so leaving it open for the whole session meant the trace only
  // showed up after the user navigated away (and even then orphaned fetch
  // spans appeared as their own "HTTP GET" traces). Ending early is fine:
  // the context manager still serves pageContext as active, so subsequent
  // fetches inherit pageSpan's trace ID and continue to appear in the same
  // trace, even though pageSpan itself is closed.
  const endPageSpan = () => pageSpan.end();
  if (document.readyState === "complete") {
    endPageSpan();
  } else {
    window.addEventListener("load", endPageSpan, { once: true });
  }

  (window as unknown as { __otelDev: { initialized: boolean } }).__otelDev.initialized = true;
  console.info(`[otel-dev] OpenTelemetry initialized → ${endpoint}, page=${location.pathname}`);
}
