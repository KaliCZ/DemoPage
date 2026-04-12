using System.Threading.RateLimiting;
using Kalandra.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string HireMeCreateUser = "hire-me-create-user";
}

public static class RateLimits
{
    // Hire-me submissions: 2 per 4 hours per authenticated user. When the
    // limit is hit, the client must re-render Turnstile in interactive mode
    // and resend with the X-Interactive-Captcha header.
    private static readonly SlidingWindowRateLimiterOptions HireMeLimiterOptions = new()
    {
        PermitLimit = 2,
        Window = TimeSpan.FromHours(4),
        SegmentsPerWindow = 24,
        QueueLimit = 0,
    };

    public static void Add(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Per-user limit: named policy applied via [EnableRateLimiting] on the endpoint.
            options.AddPolicy(RateLimitPolicies.HireMeCreateUser, httpContext =>
            {
                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().RequiredUser;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "user:" + currentUser.Id,
                    factory: _ => HireMeLimiterOptions);
            });

            // Per-IP limit: global limiter that only activates on endpoints
            // carrying the hire-me policy. Both this and the per-user policy
            // must permit the request — a single IP creating multiple accounts
            // will be caught here even though each account has its own user quota.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var rlAttribute = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
                if (rlAttribute?.PolicyName != RateLimitPolicies.HireMeCreateUser)
                    return RateLimitPartition.GetNoLimiter("no-ip-limit");

                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha-ip");

                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "ip:" + ip,
                    factory: _ => HireMeLimiterOptions);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync("{\"error\":\"captcha_required\"}", ct);
            };
        });
    }

    public static void Use(WebApplication app)
    {
        app.UseRateLimiter();
    }
}
