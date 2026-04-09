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
    // Hire-me submissions: 2 per 4 hours, applied independently per user AND
    // per client IP. Both must permit the request — stops a single user
    // hammering the form AND stops many freshly-created accounts from one IP.
    // When the limit is hit, the client must re-render Turnstile in
    // interactive mode and resend with the X-Interactive-Captcha header.
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
            options.AddPolicy(RateLimitPolicies.HireMeCreateUser, httpContext =>
            {
                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                // Endpoint is [Authorize], and UseAuthentication/UseAuthorization
                // run before UseAppRateLimits, so the user is guaranteed to be
                // signed in here. Reuse the already-built CurrentUser instead of
                // re-parsing claims.
                var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().CurrentUser;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "user:" + currentUser.Id,
                    factory: _ => HireMeLimiterOptions);
            });

            // IP limit for hire-me. Can't stack two [EnableRateLimiting] attributes,
            // so the IP limit runs as a GlobalLimiter that no-ops for any endpoint
            // not marked with the hire-me policy. GlobalLimiter and the endpoint
            // policy both have to permit the request.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!IsHireMeCreateEndpoint(httpContext))
                    return RateLimitPartition.GetNoLimiter("not-hire-me");

                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "ip:" + ip,
                    factory: _ => HireMeLimiterOptions);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"captcha_required\"}", ct);
            };
        });
    }

    public static void Use(WebApplication app)
    {
        app.UseRateLimiter();
    }

    private static bool IsHireMeCreateEndpoint(HttpContext httpContext)
    {
        var rateLimitMetadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        return rateLimitMetadata?.PolicyName == RateLimitPolicies.HireMeCreateUser;
    }
}
