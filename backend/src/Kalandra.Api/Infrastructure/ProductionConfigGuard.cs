namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Refuses to start outside Development on the committed local-dev config, so a deploy that fails to
/// inject a secret crashes at startup instead of silently running against localhost.
/// </summary>
public static class ProductionConfigGuard
{
    // Cloudflare's "always passes" Turnstile test secret; a real deploy must override it.
    private const string TurnstileTestSecret = "1x0000000000000000000000000000000AA";

    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        var problems = new List<string>();
        RequireRemote(problems, "ConnectionStrings:DefaultConnection", configuration.GetConnectionString("DefaultConnection"));
        RequireRemote(problems, "Supabase:ProjectUrl", configuration["Supabase:ProjectUrl"]);
        RequireRemote(problems, "Email:Host", configuration["Email:Host"]);

        var turnstile = configuration["Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(turnstile) || turnstile == TurnstileTestSecret)
            problems.Add("Turnstile:SecretKey is missing or the shared test key.");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"Refusing to start in the '{environment.EnvironmentName}' environment on local-dev configuration — a "
                + "deploy secret was not injected:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", problems));
    }

    private static void RequireRemote(List<string> problems, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("localhost", StringComparison.OrdinalIgnoreCase) || value.Contains("127.0.0.1"))
            problems.Add($"{key} is missing or points at localhost.");
    }
}
