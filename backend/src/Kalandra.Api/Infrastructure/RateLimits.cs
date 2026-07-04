using System.Threading.RateLimiting;
using Kalandra.Api.Infrastructure.Auth;
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

        // Blog reactions and comments: burst-friendly but bounded, per authenticated
        // user. No Turnstile escape hatch — blog writes have no captcha flow.
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
                var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().RequiredUser;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "user:" + currentUser.Id,
                    factory: _ => blogWriteLimiterOptions);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                // captcha_required is consumed by the hire-me interactive Turnstile flow;
                // blog writes have no captcha, so they get a plain rate_limited marker.
                var policyName = context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var body = policyName == RateLimitPolicies.BlogWrite
                    ? "{\"error\":\"rate_limited\"}"
                    : "{\"error\":\"captcha_required\"}";
                await context.HttpContext.Response.WriteAsync(body, ct);
            };
        });
    }

    public static void Use(WebApplication app)
    {
        app.UseRateLimiter();
    }
}
