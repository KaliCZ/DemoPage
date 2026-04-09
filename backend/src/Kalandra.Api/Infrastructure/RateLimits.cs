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
            options.AddPolicy(RateLimitPolicies.HireMeCreateUser, httpContext =>
            {
                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                var user = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().User;
                if (user == null)
                    return RateLimitPartition.GetNoLimiter("anonymous");

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: "user:" + user.Id,
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
