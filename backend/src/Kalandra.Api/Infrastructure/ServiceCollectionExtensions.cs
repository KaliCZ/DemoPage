using JasperFx.Events.Daemon;
using Kalandra.Blog.Events;
using Kalandra.Blog.Notifications;
using Kalandra.Hosting;
using Kalandra.Hosting.Auth;
using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Email;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Users;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Notifications;
using Marten;
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
        services.AddAppMartenStore(configuration, environment, ownsSchema: true)
            // SubscribeFromPresent so a first deploy doesn't re-send past notifications; HotCold so only one instance delivers during a blue/green overlap.
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
                // Supabase's auth gateway requires the `apikey` header, which AdminClient doesn't send by default.
                Headers = { ["apikey"] = serviceKey },
            };
            return new Supabase.Gotrue.AdminClient(serviceKey, options);
        });

        services.AddSingleton<IStorageService, SupabaseStorageService>();

        // Built inside the factory so replacing IUserInfoService leaves no orphaned registration to fail DI validation.
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
            // Skip the Cloudflare round-trip so local dev works offline.
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
        EmailConfig.AddSingleton(services, configuration, environment);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    public static IServiceCollection AddUserInfoCache(this IServiceCollection services, IConfiguration configuration)
    {
        // In-process fallback so tests and a bare run need no Redis.
        var redis = configuration.GetConnectionString("redis");
        if (string.IsNullOrWhiteSpace(redis))
            services.AddDistributedMemoryCache();
        else
            services.AddStackExchangeRedisCache(options => options.Configuration = redis);

        return services;
    }
}
