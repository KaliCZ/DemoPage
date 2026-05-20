// Dev-only OTel: dynamic-imported under import.meta.env.DEV so Vite tree-shakes it out of prod (Sentry's loader owns prod).
// Emits to the Aspire dashboard via PUBLIC_OTLP_TRACES_ENDPOINT, which the AppHost injects per-instance.

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

// Exposed on window for devtools inspection — import.meta isn't reachable from the console.
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

  // Hands out pageContext when nothing else is active so stray fetches inherit the page span instead of starting new roots.
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
        // Propagate traceparent to anything on localhost (Vite proxy + direct backend hits).
        propagateTraceHeaderCorsUrls: [/^https?:\/\/localhost(:\d+)?\//, /^https?:\/\/127\.0\.0\.1(:\d+)?\//],
        // Skip telemetry-of-telemetry to avoid self-loops.
        ignoreUrls: [new RegExp("^" + endpoint.replace(/[/.]/g, "\\$&")), /betterstackdata\.com\//, /betterstack\.net\//, /sentry\.io\//],
        // Rename "HTTP GET" → "<METHOD> <path>" so the dashboard list is scannable.
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

  // Page-view span becomes the implicit root so every trace is "page /<pathname>" instead of the first fetch.
  const tracer = trace.getTracer("web");
  const pageSpan = tracer.startSpan(`page ${location.pathname}`, { kind: SpanKind.INTERNAL });
  contextManager.setPageContext(trace.setSpan(contextManager.active(), pageSpan));

  // End at `load`, not pagehide — BatchSpanProcessor only exports closed spans, and the manager still serves pageContext after end().
  const endPageSpan = () => pageSpan.end();
  if (document.readyState === "complete") {
    endPageSpan();
  } else {
    window.addEventListener("load", endPageSpan, { once: true });
  }

  (window as unknown as { __otelDev: { initialized: boolean } }).__otelDev.initialized = true;
  console.info(`[otel-dev] OpenTelemetry initialized → ${endpoint}, page=${location.pathname}`);
}
