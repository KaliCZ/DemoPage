// Dev-only OpenTelemetry initialization. Imported via a dynamic import
// gated on import.meta.env.DEV in Layout.astro, so Vite tree-shakes the
// whole module — and its ~100KB of @opentelemetry/* deps — out of the
// production bundle. In prod, BetterStack/Sentry owns observability.
//
// Emits to the Aspire dashboard's OTLP HTTP endpoint. The URL is injected
// by the AppHost via PUBLIC_OTLP_TRACES_ENDPOINT (it varies per AppHost
// instance because the OTLP port is dynamically reserved).

import { context, DiagConsoleLogger, DiagLogLevel, diag, SpanKind, trace } from "@opentelemetry/api";
import { ZoneContextManager } from "@opentelemetry/context-zone";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { registerInstrumentations } from "@opentelemetry/instrumentation";
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load";
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchSpanProcessor, WebTracerProvider } from "@opentelemetry/sdk-trace-web";
import { ATTR_SERVICE_NAME } from "@opentelemetry/semantic-conventions";

const endpoint = import.meta.env.PUBLIC_OTLP_TRACES_ENDPOINT;
if (!endpoint) {
  console.warn("[otel-dev] PUBLIC_OTLP_TRACES_ENDPOINT is not set — frontend spans will not be emitted.");
} else {
  // Surface SDK internals — export attempts, exporter errors, CORS
  // rejections — straight to the browser console. Dev-only, so verbosity
  // is fine.
  diag.setLogger(new DiagConsoleLogger(), DiagLogLevel.INFO);

  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: "kalandra-frontend",
    }),
    spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: endpoint }))],
  });
  provider.register({
    contextManager: new ZoneContextManager(),
  });

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        // Same-origin (Astro dev proxy) and our backend. Without this list
        // the auto-instrumentation only adds traceparent to same-origin
        // fetches, which is usually fine in dev since the Vite proxy keeps
        // /api/* on the page origin.
        propagateTraceHeaderCorsUrls: [/^https?:\/\/localhost(:\d+)?\//, /^https?:\/\/127\.0\.0\.1(:\d+)?\//],
      }),
    ],
  });

  // Start a deliberate page-view span as the trace root, then bind window.fetch
  // to a context where it's the active span. Without this, the first fetch on
  // the page (typically the /health warm-up) becomes the trace root and every
  // subsequent fetch chains under it — the trace ends up named "GET /health"
  // for every page. Now every fetch on the page is a child of the page span,
  // so the trace is named after the route the user is actually on.
  const tracer = trace.getTracer("kalandra-frontend");
  const pageSpan = tracer.startSpan(`page ${location.pathname}`, { kind: SpanKind.INTERNAL });
  const pageContext = trace.setSpan(context.active(), pageSpan);
  window.fetch = context.bind(pageContext, window.fetch);

  // End the page span when the page unloads so the trace flushes. `pagehide`
  // fires for both regular navigations and back/forward cache evictions.
  window.addEventListener("pagehide", () => pageSpan.end(), { once: true });

  console.info(`[otel-dev] OpenTelemetry initialized → ${endpoint}, page=${location.pathname}`);
}
