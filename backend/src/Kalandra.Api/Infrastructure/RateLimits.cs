using System.Threading.RateLimiting;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string HireMeCreateUser = "hire-me-create-user";
    public const string BlogWrite = "blog-write";
}

public static class RateLimits
{
    public static void Add(IServiceCollection services, IHostEnvironment environment)
    {
        var permitLimit = environment.IsDevelopment() ? 50 : 2;

        // Hire-me submissions: N per 4 hours per authenticated user. When the
        // limit is hit, the client must re-render Turnstile in interactive mode
        // and resend with the X-Interactive-Captcha header.
        var hireMeLimiterOptions = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromHours(4),
            SegmentsPerWindow = 24,
            QueueLimit = 0,
        };

        // No Turnstile escape hatch here — blog writes have no captcha flow.
        var blogWriteLimiterOptions = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = environment.IsDevelopment() ? 1000 : 30,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        };

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.HireMeCreateUser, httpContext =>
            {
                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().RequiredUser;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "user:" + currentUser.Id,
                    factory: _ => hireMeLimiterOptions);
            });

            options.AddPolicy(RateLimitPolicies.BlogWrite, httpContext =>
            {
                // Anonymous blog writes (views, reactions) key on the client-minted visitor id.
                // It's spoofable, but these are low-value analytics writes, and the alternative —
                // one shared anonymous bucket — would let a single caller starve every visitor.
                var user = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().User;
                var partitionKey = user is { } signedIn
                    ? "user:" + signedIn.Id
                    : "visitor:" + VisitorPartition(httpContext);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => blogWriteLimiterOptions);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                // captcha_required is consumed by the hire-me interactive Turnstile flow; everything
                // else (blog writes, MCP) has no captcha, so it gets a plain rate_limited marker.
                var policyName = context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var body = policyName == RateLimitPolicies.HireMeCreateUser
                    ? "{\"error\":\"captcha_required\"}"
                    : "{\"error\":\"rate_limited\"}";
                await context.HttpContext.Response.WriteAsync(body, ct);
            };
        });
    }

    public static void Use(WebApplication app)
    {
        app.UseRateLimiter();
    }

    private static string VisitorPartition(HttpContext httpContext)
    {
        var visitorId = httpContext.Request.Headers["X-Visitor-Id"].ToString();
        return string.IsNullOrEmpty(visitorId) ? "anonymous" : visitorId;
    }
}
