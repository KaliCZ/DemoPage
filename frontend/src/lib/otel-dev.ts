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
  // Surface exporter errors and CORS rejections to the browser console.
  diag.setLogger(new DiagConsoleLogger(), DiagLogLevel.WARN);

  // Context manager that hands out pageContext whenever nothing else is
  // active, so fetches outside an explicit `context.with(...)` scope
  // inherit the page span as their parent instead of starting their own
  // root and producing one "HTTP GET" trace per request.
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
        // Propagate traceparent to anything on localhost — covers the
        // Astro dev proxy and direct backend hits in dev.
        propagateTraceHeaderCorsUrls: [/^https?:\/\/localhost(:\d+)?\//, /^https?:\/\/127\.0\.0\.1(:\d+)?\//],
        // Skip telemetry-of-telemetry: the exporter's own POSTs (would
        // self-loop) and BetterStack/Sentry ingestion.
        ignoreUrls: [new RegExp("^" + endpoint.replace(/[/.]/g, "\\$&")), /betterstackdata\.com\//, /betterstack\.net\//, /sentry\.io\//],
        // Rename spans from the default "HTTP GET" to "<METHOD> <path>"
        // so the dashboard list is scannable. Handles Request, URL, and
        // plain-string inputs; the query string is dropped.
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

  // Start the page-view span and install it as the context manager's
  // implicit floor — so every trace roots at "page /<pathname>" instead
  // of whichever fetch fires first.
  const tracer = trace.getTracer("web");
  const pageSpan = tracer.startSpan(`page ${location.pathname}`, { kind: SpanKind.INTERNAL });
  contextManager.setPageContext(trace.setSpan(contextManager.active(), pageSpan));

  // End pageSpan at `load`, not at pagehide: BatchSpanProcessor only
  // exports a span once it ends, so an open page span would delay the
  // whole trace until navigation away. The context manager keeps serving
  // pageContext after end(), so later fetches still inherit its trace ID.
  const endPageSpan = () => pageSpan.end();
  if (document.readyState === "complete") {
    endPageSpan();
  } else {
    window.addEventListener("load", endPageSpan, { once: true });
  }

  (window as unknown as { __otelDev: { initialized: boolean } }).__otelDev.initialized = true;
  console.info(`[otel-dev] OpenTelemetry initialized → ${endpoint}, page=${location.pathname}`);
}
