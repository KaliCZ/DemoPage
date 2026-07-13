using Npgsql;
using OpenTelemetry;
using Sentry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// Sentry + OpenTelemetry wiring for the MCP host, mirroring Kalandra.Api's setup under the "mcp"
/// service name (the BetterStack paths the API keeps disabled are omitted here).
/// </summary>
public static class Observability
{
    private const string ServiceName = "mcp";

    public static void Add(WebApplicationBuilder builder)
    {
        var sentryConfig = SentryConfig.AddOptionalSingleton(builder.Services, builder.Configuration);

        if (sentryConfig is null && builder.Environment.IsProduction())
            throw new InvalidOperationException("Sentry:Dsn must be configured in production.");

        if (sentryConfig is not null)
            AddSentry(builder, sentryConfig);

        AddOpenTelemetry(builder, sentryConfig);
    }

    private static void AddSentry(WebApplicationBuilder builder, SentryConfig config)
    {
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = config.Dsn.Value;
            options.Release = AppVersion.InformationalVersion;

            // UseOtlp sends traces to Sentry as raw OTel spans (via AddSentryOtlpExporter below) and turns
            // off the SDK's own request tracing, so the OTel pipeline isn't duplicated on Sentry's side.
            options.UseOtlp();
            options.DisableSentryHttpMessageHandler = true;

            options.EnableLogs = true;
            options.MinimumEventLevel = LogLevel.Warning;
            options.MinimumBreadcrumbLevel = LogLevel.Information;

            options.SampleRate = 1.0f;
            options.SendDefaultPii = true;
            options.CaptureFailedRequests = false;

            // Client disconnects and cancelled requests aren't actionable.
            options.AddExceptionFilterForType<OperationCanceledException>();
        });
    }

    private static void AddOpenTelemetry(WebApplicationBuilder builder, SentryConfig? sentryConfig)
    {
        // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT; the default AddOtlpExporter() picks it up.
        var aspireEnabled = !string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (sentryConfig is null && !aspireEnabled)
            return;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: ServiceName, serviceVersion: AppVersion.InformationalVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri is not { Host: var host }
                            || (!host.EndsWith("sentry.io", StringComparison.OrdinalIgnoreCase)
                                && !host.EndsWith("ingest.sentry.io", StringComparison.OrdinalIgnoreCase));
                    })
                    .AddSource("Marten")
                    .AddNpgsql();

                if (sentryConfig is not null)
                    tracing.AddSentryOtlpExporter(sentryConfig.Dsn.Value);

                if (aspireEnabled)
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Marten");

                if (aspireEnabled)
                    metrics.AddOtlpExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;

            if (aspireEnabled)
                logging.AddOtlpExporter();
        });
    }
}
