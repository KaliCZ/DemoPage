using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kalandra.Api.Infrastructure;

public static class Observability
{
    private const string ServiceName = "kalandra-api";

    public static void Add(WebApplicationBuilder builder, BetterStackConfig? config)
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
