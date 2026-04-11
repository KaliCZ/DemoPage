using System.Reflection;
using OpenTelemetry;
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
        var sourceToken = builder.Configuration["BetterStack:SourceToken"];

        if (string.IsNullOrEmpty(sourceToken))
            return;

        var endpoint = new Uri("https://in-otel.logs.betterstack.com");
        var headers = $"Authorization=Bearer {sourceToken}";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: ServiceName,
                    serviceVersion: typeof(Observability).Assembly
                        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion ?? "unknown"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = endpoint;
                    otlp.Headers = headers;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = endpoint;
                    otlp.Headers = headers;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                }));

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = endpoint;
                otlp.Headers = headers;
                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
        });
    }
}
