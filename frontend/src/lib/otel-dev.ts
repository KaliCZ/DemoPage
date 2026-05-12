// Dev-only OpenTelemetry initialization. Imported via a dynamic import
// gated on import.meta.env.DEV in Layout.astro, so Vite tree-shakes the
// whole module — and its ~100KB of @opentelemetry/* deps — out of the
// production bundle. In prod, BetterStack/Sentry owns observability.
//
// Emits to the Aspire dashboard's OTLP HTTP endpoint. The URL is injected
// by the AppHost via PUBLIC_OTLP_TRACES_ENDPOINT (it varies per AppHost
// instance because the OTLP port is dynamically reserved).

import { context, trace } from "@opentelemetry/api";
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

  // Tag the global tracer with a fingerprint so it's obvious in console
  // that the init ran (helpful when debugging "why no spans?").
  // eslint-disable-next-line no-console
  console.info("[otel-dev] OpenTelemetry initialized → " + endpoint);
  void context;
  void trace;
}
