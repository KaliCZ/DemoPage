using JasperFx;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Avatars;
using Kalandra.JobOffers;
using Marten;

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
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddStorageServices(this IServiceCollection services)
    {
        services.AddHttpClient<IStorageService, SupabaseStorageService>();
        services.AddSingleton<IAvatarService, SupabaseAvatarService>();
        services.AddSingleton<Features.Profile.UploadAvatarHandler>();

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<Kalandra.Infrastructure.Configuration.SupabaseAuthConfig>();
            var client = new Supabase.Client(
                config.ProjectUrl.Value,
                config.ServiceKey.Value,
                new Supabase.SupabaseOptions { AutoConnectRealtime = false });
            client.InitializeAsync().Wait();
            return client;
        });

        return services;
    }

    public static IServiceCollection AddTurnstile(this IServiceCollection services)
    {
        services.AddHttpClient<ITurnstileValidator, TurnstileValidator>();

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
