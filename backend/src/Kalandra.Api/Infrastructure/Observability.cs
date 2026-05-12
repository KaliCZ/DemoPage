using Npgsql;
using OpenTelemetry;
using Sentry.OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kalandra.Api.Infrastructure;

public static class Observability
{
    private const string ServiceName = "api";

    public static void Add(WebApplicationBuilder builder)
    {
        var sentryConfig = SentryConfig.AddOptionalSingleton(builder.Services, builder.Configuration);
        var betterStackConfig = BetterStackConfig.AddOptionalSingleton(builder.Services, builder.Configuration);

        AddSentry(builder, sentryConfig);
        AddOpenTelemetry(builder, betterStackConfig);
    }

    private static void AddSentry(WebApplicationBuilder builder, SentryConfig? config)
    {
        if (config is null)
            return;

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = config.Dsn.Value;
            options.Release = AppVersion.InformationalVersion;

            // Bridge with the existing OTEL pipeline so Sentry and OTEL share trace context.
            options.UseOpenTelemetry();
            options.DisableDiagnosticSourceIntegration();
            options.DisableSentryHttpMessageHandler = true;

            // Logging — only capture warnings and above; info-level goes to BetterStack via OTEL.
            options.EnableLogs = true;
            options.MinimumEventLevel = LogLevel.Warning;

            options.TracesSampleRate = 1.0;
            options.SampleRate = 1.0f;
            options.SendDefaultPii = true;
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.Medium;
            options.CaptureFailedRequests = false;

            // Filter noise — client disconnects and cancelled requests aren't actionable.
            options.AddExceptionFilterForType<OperationCanceledException>();
            options.SetBeforeSend((sentryEvent, _) =>
            {
                if (sentryEvent.Exception is BadHttpRequestException bre
                    && bre.Message.Contains("Unexpected end of request content"))
                    return null;

                return sentryEvent;
            });
        });
    }

    private static void AddOpenTelemetry(WebApplicationBuilder builder, BetterStackConfig? config)
    {
        // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT (and friends) when the API is
        // launched under the AppHost. The default AddOtlpExporter() reads those.
        var aspireEnabled = !string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (config is null && !aspireEnabled)
        {
            if (builder.Environment.IsProduction())
                throw new InvalidOperationException("BetterStack:SourceToken and BetterStack:OtlpEndpoint must be configured in production.");

            return;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: ServiceName,
                    serviceVersion: AppVersion.InformationalVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri is not { Host: var host }
                            || !host.EndsWith("betterstackdata.com", StringComparison.OrdinalIgnoreCase);
                    })
                    .AddNpgsql();

                if (config is not null)
                    tracing.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/traces");
                        otlp.Headers = config.AuthorizationHeader;
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });

                if (aspireEnabled)
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (config is not null)
                    metrics.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/metrics");
                        otlp.Headers = config.AuthorizationHeader;
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });

                if (aspireEnabled)
                    metrics.AddOtlpExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;

            if (config is not null)
                logging.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/logs");
                    otlp.Headers = config.AuthorizationHeader;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });

            if (aspireEnabled)
                logging.AddOtlpExporter();
        });
    }
}
