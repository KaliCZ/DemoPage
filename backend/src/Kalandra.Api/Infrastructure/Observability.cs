using Kalandra.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        if (sentryConfig is null && builder.Environment.IsProduction())
            throw new InvalidOperationException("Sentry:Dsn must be configured in production.");

        CapHealthCheckLogSeverity(builder.Services);
        AddSentry(builder, sentryConfig);
        AddOpenTelemetry(builder, sentryConfig, betterStackConfig);
    }

    // DefaultHealthCheckService logs unhealthy probes at Error/Warning, but a failing probe is
    // expected signal (/health alerting is Better Stack's job) — cap the category at Information
    // so the entries stay in every sink without tripping error alerting such as Sentry issues.
    private static void CapHealthCheckLogSeverity(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(serviceProvider =>
            new LevelCappingLoggerFactory(
                new LoggerFactory(
                    serviceProvider.GetServices<ILoggerProvider>(),
                    serviceProvider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
                    serviceProvider.GetRequiredService<IOptions<LoggerFactoryOptions>>(),
                    serviceProvider.GetService<IExternalScopeProvider>()),
                categoryPrefix: "Microsoft.Extensions.Diagnostics.HealthChecks",
                maximumLevel: LogLevel.Information)));

    private static void AddSentry(WebApplicationBuilder builder, SentryConfig? config)
    {
        if (config is null)
            return;

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = config.Dsn.Value;
            options.Release = AppVersion.InformationalVersion;

            // Bridge with the OTEL pipeline so Sentry receives spans through Sentry.OpenTelemetry.
            options.UseOpenTelemetry();
            options.DisableDiagnosticSourceIntegration();
            options.DisableSentryHttpMessageHandler = true;

            // EnableLogs wires Sentry.AspNetCore's structured logger provider so ILogger calls flow into
            // Sentry's Logs product. Issues fire from Warning+ and unhandled exceptions; Info+ becomes
            // breadcrumbs attached to those issues; everything Info+ also lands as a structured log.
            options.EnableLogs = true;
            options.MinimumEventLevel = LogLevel.Warning;
            options.MinimumBreadcrumbLevel = LogLevel.Information;

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

    private static void AddOpenTelemetry(WebApplicationBuilder builder, SentryConfig? sentryConfig, BetterStackConfig? betterStackConfig)
    {
        // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT; the default AddOtlpExporter() picks it up.
        var aspireEnabled = !string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (sentryConfig is null && betterStackConfig is null && !aspireEnabled)
            return;

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
                            || (!host.EndsWith("betterstackdata.com", StringComparison.OrdinalIgnoreCase)
                                && !host.EndsWith("sentry.io", StringComparison.OrdinalIgnoreCase)
                                && !host.EndsWith("ingest.sentry.io", StringComparison.OrdinalIgnoreCase));
                    })
                    .AddSource("Marten")
                    .AddNpgsql();

                if (sentryConfig is not null)
                    tracing.AddSentry();

                if (betterStackConfig is not null)
                    tracing.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(betterStackConfig.OtlpEndpoint, "v1/traces");
                        otlp.Headers = betterStackConfig.AuthorizationHeader;
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
                    .AddRuntimeInstrumentation()
                    .AddMeter("Marten");

                if (betterStackConfig is not null)
                    metrics.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(betterStackConfig.OtlpEndpoint, "v1/metrics");
                        otlp.Headers = betterStackConfig.AuthorizationHeader;
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });

                if (aspireEnabled)
                    metrics.AddOtlpExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;

            if (betterStackConfig is not null)
                logging.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(betterStackConfig.OtlpEndpoint, "v1/logs");
                    otlp.Headers = betterStackConfig.AuthorizationHeader;
                    otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });

            if (aspireEnabled)
                logging.AddOtlpExporter();
        });
    }
}
