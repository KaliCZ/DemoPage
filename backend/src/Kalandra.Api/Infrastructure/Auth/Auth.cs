using System.Security.Claims;
using System.Text.Json;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kalandra.Api.Infrastructure.Auth;

public static class AuthPolicies
{
    public const string Admin = "admin";
}

public static class Auth
{
    public static void Add(IServiceCollection services, SupabaseConfig supabaseConfig)
    {
        var projectUrl = supabaseConfig.ProjectUrl.Value.TrimEnd('/');
        var issuer = $"{projectUrl}/auth/v1";
        var metadataAddress = $"{issuer}/.well-known/openid-configuration";
        var requireHttps = projectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RefreshOnIssuerKeyNotFound = true;
                options.RequireHttpsMetadata = requireHttps;
                options.MetadataAddress = metadataAddress;
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadataAddress: metadataAddress,
                    configRetriever: new OpenIdConnectConfigurationRetriever(),
                    docRetriever: new HttpDocumentRetriever { RequireHttps = requireHttps });

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception is SecurityTokenSignatureKeyNotFoundException)
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Kalandra.Api.Auth");

                            logger.LogError(
                                context.Exception,
                                "JWT validation failed because no signing keys were available for issuer {Issuer}. " +
                                "Check Supabase JWKS availability and Auth configuration.",
                                issuer);
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity == null)
                            return Task.CompletedTask;

                        ExtractRolesFromAppMetadata(context.Principal!, identity);

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole(nameof(UserRole.Admin)));
    }

    public static void Use(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    /// <summary>
    /// Canonicalizes roles to the UserRole spelling so RequireRole and CurrentUser can compare against nameof(UserRole.X) regardless of wire casing.
    /// </summary>
    private static void ExtractRolesFromAppMetadata(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var appMetadata = principal.FindFirstValue("app_metadata");
        if (string.IsNullOrEmpty(appMetadata))
            return;

        using var doc = JsonDocument.Parse(appMetadata);

        if (doc.RootElement.TryGetProperty("roles", out var rolesProp) &&
            rolesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rolesProp.EnumerateArray())
            {
                var raw = item.GetString();
                if (!string.IsNullOrEmpty(raw) && Enum.TryParse<UserRole>(raw, ignoreCase: true, out var role))
                    identity.AddClaim(new Claim(ClaimTypes.Role, role.ToString()));
            }
        }
    }
}
