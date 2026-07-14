using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Users;
using Microsoft.Extensions.Caching.Distributed;

namespace Kalandra.McpServer.Infrastructure;

public static class McpServices
{
    /// <summary>
    /// Request-scoped identity plus the Supabase-backed services the tools' domain handlers depend on:
    /// IStorageService (CreateJobOfferHandler) and IUserInfoService (resolving comment authors). These
    /// registrations mirror the API's for the same handlers.
    /// </summary>
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddSingleton(TimeProvider.System);

        // AddBlogDomain's BlogCommentCountCache is backed by IMemoryCache.
        services.AddMemoryCache();

        // In-process cache is enough here; the API may share a Redis cache, but user info is re-fetchable.
        services.AddDistributedMemoryCache();

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<SupabaseConfig>();
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
            var config = sp.GetRequiredService<SupabaseConfig>();
            var projectUrl = config.ProjectUrl.Value.TrimEnd('/');
            var serviceKey = config.ServiceKey.Value;
            var options = new Supabase.Gotrue.ClientOptions
            {
                Url = $"{projectUrl}/auth/v1",
                Headers = { ["apikey"] = serviceKey },
            };
            return new Supabase.Gotrue.AdminClient(serviceKey, options);
        });

        services.AddSingleton<IStorageService, SupabaseStorageService>();

        services.AddSingleton<IUserInfoService>(sp => new CachingUserInfoService(
            ActivatorUtilities.CreateInstance<SupabaseUserInfoService>(sp),
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetRequiredService<ILogger<CachingUserInfoService>>()));

        return services;
    }
}
