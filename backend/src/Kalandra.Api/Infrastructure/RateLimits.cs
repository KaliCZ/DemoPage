using System.Threading.RateLimiting;
using Kalandra.Api.Infrastructure.Auth;

namespace Kalandra.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string HireMeCreateUser = "hire-me-create-user";
}

public static class RateLimits
{
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimit:HireMePermitLimit", defaultValue: 2);

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
