using OpenTelemetry;
using Sentry.OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kalandra.Api.Infrastructure;

public static class Observability
{
    private const string ServiceName = "kalandra-api";

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
        if (config is null)
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
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Marten")
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/traces");
                    otlp.Headers = config.AuthorizationHeader;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Marten")
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/metrics");
                    otlp.Headers = config.AuthorizationHeader;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                }));

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(config.OtlpEndpoint, "v1/logs");
                otlp.Headers = config.AuthorizationHeader;
                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
        });
    }
}
