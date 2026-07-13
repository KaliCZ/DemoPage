using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.OpenTelemetry;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog;
using Kalandra.Blog.Events;
using Kalandra.Blog.Notifications;
using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Email;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Users;
using Kalandra.JobOffers;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Notifications;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Kalandra.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppMarten(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        if (!environment.IsDevelopment()
            && (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) || connectionString.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection still points at localhost — a production deploy must set the real database.");

        services.AddMarten(options =>
        {
            options.Connection(connectionString);

            // Domain-specific Marten configuration
            options.ConfigureJobOffers();
            options.ConfigureBlog();

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

            // Emit Marten session/batch spans so connection + batch wait time isn't a blind gap on the trace.
            options.OpenTelemetry.TrackConnections = TrackLevel.Normal;
            options.OpenTelemetry.TrackEventCounters();
        })
        .UseLightweightSessions()
        // Notification emails are delivered by subscriptions reacting to committed events, run by the
        // async daemon. SubscribeFromPresent so a first deploy doesn't replay history and re-send every
        // past notification; HotCold so only one instance delivers during a blue/green overlap.
        .AddSubscriptionWithServices<BlogCommentNotificationSubscription>(ServiceLifetime.Scoped, o =>
        {
            o.IncludeType<BlogCommentPosted>();
            o.Options.SubscribeFromPresent();
        })
        .AddSubscriptionWithServices<JobOfferNotificationSubscription>(ServiceLifetime.Scoped, o =>
        {
            o.IncludeType<JobOfferSubmitted>();
            o.IncludeType<JobOfferCommentAdded>();
            o.Options.SubscribeFromPresent();
        })
        .AddAsyncDaemon(DaemonMode.HotCold);

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

        // The source is built inside the factory (not as its own registration) so a test swapping
        // IUserInfoService for a fake doesn't leave a stray source that fails DI validation.
        services.AddSingleton<IUserInfoService>(sp => new CachingUserInfoService(
            ActivatorUtilities.CreateInstance<SupabaseUserInfoService>(sp),
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetRequiredService<ILogger<CachingUserInfoService>>()));

        return services;
    }

    public static IServiceCollection AddTurnstile(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            // Skip the Cloudflare round-trip locally so dev works offline; the test key would have rubber-stamped it anyway.
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

    public static IServiceCollection AddEmailServices(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // appsettings.json defaults to the local mail catcher; production overrides Host/Port/Username/Password via env.
        EmailConfig.AddSingleton(services, configuration, environment);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    public static IServiceCollection AddUserInfoCache(this IServiceCollection services, IConfiguration configuration)
    {
        // Redis when configured; otherwise an in-process distributed cache so tests and a bare run need no Redis.
        var redis = configuration.GetConnectionString("redis");
        if (string.IsNullOrWhiteSpace(redis))
            services.AddDistributedMemoryCache();
        else
            services.AddStackExchangeRedisCache(options => options.Configuration = redis);

        return services;
    }
}
