using JasperFx;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Users;
using Kalandra.JobOffers;
using Marten;
using Marten.Services;

namespace Kalandra.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppMarten(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        services.AddMarten(options =>
        {
            options.Connection(connectionString);

            // Domain-specific Marten configuration
            options.ConfigureJobOffers();

            // Use snake_case for database identifiers
            options.UseSystemTextJsonForSerialization();

            if (environment.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.All;
            }
            else
            {
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            }

            // Emit Marten's high-level operation spans so each session shows
            // up on the dashboard with the actual SQL statements as
            // attributes (Marten attaches db.statement directly on its
            // span). Observability.cs subscribes via AddSource("Marten").
            options.OpenTelemetry.TrackConnections = TrackLevel.Normal;
            options.OpenTelemetry.TrackEventCounters();
        })
        .UseLightweightSessions();

        return services;
    }

    public static IServiceCollection AddAppCors(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("DefaultPolicy", policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(origin =>
                    {
                        var host = new Uri(origin).Host;
                        return host is "localhost" or "127.0.0.1";
                    });
                }
                else
                {
                    policy.WithOrigins("https://www.kalandra.tech");
                }

                policy
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromHours(24));
            });
        });

        return services;
    }

    public static IServiceCollection AddStorageServices(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<Kalandra.Infrastructure.Configuration.SupabaseConfig>();
            var projectUrl = config.ProjectUrl.Value.TrimEnd('/');
            var serviceKey = config.ServiceKey.Value;
            var headers = new Dictionary<string, string>
            {
                ["apikey"] = serviceKey,
                ["Authorization"] = $"Bearer {serviceKey}",
            };
            return new Supabase.Storage.Client($"{projectUrl}/storage/v1", headers);
        });

        services.AddSingleton<Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User>>(sp =>
        {
            var config = sp.GetRequiredService<Kalandra.Infrastructure.Configuration.SupabaseConfig>();
            var projectUrl = config.ProjectUrl.Value.TrimEnd('/');
            var serviceKey = config.ServiceKey.Value;
            var options = new Supabase.Gotrue.ClientOptions
            {
                Url = $"{projectUrl}/auth/v1",
                // Supabase's auth gateway requires the `apikey` header on every request.
                // AdminClient only sends `Authorization: Bearer <serviceKey>` by default,
                // so we inject apikey here. For `sb_secret_…` keys the Authorization header
                // is accepted as an opaque token rather than JWT-verified.
                Headers = { ["apikey"] = serviceKey },
            };
            return new Supabase.Gotrue.AdminClient(serviceKey, options);
        });

        services.AddSingleton<IStorageService, SupabaseStorageService>();
        services.AddSingleton<IUserInfoService, SupabaseUserInfoService>();

        return services;
    }

    public static IServiceCollection AddTurnstile(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            // Skip the Cloudflare round-trip locally so dev works offline.
            // The test secret key would have made Cloudflare rubber-stamp
            // the response anyway; this just removes the network call.
            services.AddSingleton<ITurnstileValidator, AlwaysPassTurnstileValidator>();
        }
        else
        {
            services.AddHttpClient<ITurnstileValidator, TurnstileValidator>();
        }

        return services;
    }

    public static IServiceCollection AddAuthAdminServices(this IServiceCollection services)
    {
        services.AddHttpClient<ISupabaseAdminService, SupabaseAdminService>();

        return services;
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
