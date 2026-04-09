using System.Threading.RateLimiting;
using Kalandra.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string HireMeCreate = "hire-me-create";
}

public static class RateLimits
{
    public static void Add(IServiceCollection services)
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

                // Endpoint is [Authorize], and UseAuthentication/UseAuthorization
                // run before UseAppRateLimits, so the user is guaranteed to be
                // signed in here. Reuse the already-built CurrentUser instead of
                // re-parsing claims.
                var currentUser = httpContext.RequestServices
                    .GetRequiredService<ICurrentUserAccessor>()
                    .CurrentUser;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: currentUser.Id,
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
    }

    public static void Use(WebApplication app)
    {
        app.UseRateLimiter();
    }
}
