using JasperFx;
using JasperFx.OpenTelemetry;
using Kalandra.Blog;
using Kalandra.JobOffers;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Hosting;

public static class AppMarten
{
    /// <summary>
    /// The Marten store both hosts share — same connection, same domain config, same serialization, so
    /// they read and write one schema. Returns the Marten builder so a host can chain what only it runs:
    /// Kalandra.Api adds the notification subscriptions and the async daemon, and the MCP host must not,
    /// or a tool-written event would be emailed twice.
    /// </summary>
    /// <param name="ownsSchema">Only the owner migrates in production; the other host assumes the schema exists.</param>
    public static MartenServiceCollectionExtensions.MartenConfigurationExpression AddAppMartenStore(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        bool ownsSchema)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        if (!environment.IsDevelopment()
            && (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) || connectionString.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection still points at localhost — a production deploy must set the real database.");

        return services.AddMarten(options =>
        {
            options.Connection(connectionString);

            // Domain-specific Marten configuration
            options.ConfigureJobOffers();
            options.ConfigureBlog();

            options.UseSystemTextJsonForSerialization();

            options.AutoCreateSchemaObjects = environment.IsDevelopment()
                ? AutoCreate.All
                : ownsSchema
                    ? AutoCreate.CreateOrUpdate
                    : AutoCreate.None;

            // Emit Marten session/batch spans so connection + batch wait time isn't a blind gap on the trace.
            options.OpenTelemetry.TrackConnections = TrackLevel.Normal;
            options.OpenTelemetry.TrackEventCounters();
        })
        .UseLightweightSessions();
    }
}
