using JasperFx;
using JasperFx.OpenTelemetry;
using Kalandra.Blog;
using Kalandra.JobOffers;
using Marten;

namespace Kalandra.McpServer.Infrastructure;

public static class McpMarten
{
    /// <summary>
    /// Registers the Marten store the tools read and write through. It deliberately does NOT run the async
    /// daemon or notification subscriptions — those live only in Kalandra.Api, so a comment or offer created
    /// via a tool is emailed exactly once, by that host's daemon reacting to the shared event store.
    /// The store options mirror Kalandra.Api's AddAppMarten (serialization + domain config) so both hosts
    /// read and write the same schema.
    /// </summary>
    public static IServiceCollection AddMcpMarten(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        if (!environment.IsDevelopment()
            && (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) || connectionString.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection still points at localhost — a production deploy must set the real database.");

        services.AddMarten(options =>
        {
            options.Connection(connectionString);
            options.ConfigureJobOffers();
            options.ConfigureBlog();
            options.UseSystemTextJsonForSerialization();

            // Kalandra.Api owns schema creation; in production this host assumes the schema already exists.
            options.AutoCreateSchemaObjects = environment.IsDevelopment() ? AutoCreate.All : AutoCreate.None;

            options.OpenTelemetry.TrackConnections = TrackLevel.Normal;
            options.OpenTelemetry.TrackEventCounters();
        })
        .UseLightweightSessions();

        return services;
    }
}
