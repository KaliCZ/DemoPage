using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string HireMeCreate = "hire-me-create";
}

public static class RateLimits
{
    public static IServiceCollection AddAppRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // Hire-me submissions: 2 per 4 hours per authenticated user. When the
            // limit is hit, the client must re-render Turnstile in interactive mode
            // and resend the request with the X-Interactive-Captcha header to bypass.
            options.AddPolicy(RateLimitPolicies.HireMeCreate, httpContext =>
            {
                if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
                    return RateLimitPartition.GetNoLimiter("interactive-captcha");

                var partitionKey =
                    httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromHours(4),
                        SegmentsPerWindow = 24,
                        QueueLimit = 0,
                    });
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"captcha_required\"}", ct);
            };
        });

        return services;
    }

    public static IApplicationBuilder UseAppRateLimits(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }
}
