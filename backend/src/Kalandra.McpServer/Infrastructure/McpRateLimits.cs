using System.Threading.RateLimiting;
using Kalandra.Infrastructure.Auth;

namespace Kalandra.McpServer.Infrastructure;

public static class McpRateLimitPolicies
{
    public const string Mcp = "mcp";
}

public static class McpRateLimits
{
    public static void Add(IServiceCollection services, IHostEnvironment environment)
    {
        // One generous bucket for the whole endpoint: an assistant session lists tools and makes several
        // calls in quick succession. Signed-in calls key on the user; the rest on IP.
        var mcpLimiterOptions = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = environment.IsDevelopment() ? 1000 : 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        };

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(McpRateLimitPolicies.Mcp, httpContext =>
            {
                var user = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>().User;
                var partitionKey = user is { } signedIn
                    ? "user:" + signedIn.Id
                    : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => mcpLimiterOptions);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync("{\"error\":\"rate_limited\"}", ct);
            };
        });
    }

    public static void Use(WebApplication app) => app.UseRateLimiter();
}
