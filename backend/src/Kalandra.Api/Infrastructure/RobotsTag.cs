namespace Kalandra.Api.Infrastructure;

public static class RobotsTag
{
    public static void Use(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            await next();
        });
    }
}
