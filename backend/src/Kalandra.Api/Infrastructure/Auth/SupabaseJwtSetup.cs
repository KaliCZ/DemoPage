using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kalandra.Api.Infrastructure.Auth;

public static class SupabaseJwtSetup
{
    public static IServiceCollection AddSupabaseAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = new SupabaseJwtOptions();
        configuration.GetSection(SupabaseJwtOptions.SectionName).Bind(authOptions);
        services.Configure<SupabaseJwtOptions>(configuration.GetSection(SupabaseJwtOptions.SectionName));

        var projectUrl = authOptions.SupabaseProjectUrl.TrimEnd('/');
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
                        if (IsMissingSigningKeyFailure(context.Exception))
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
                        ExtractDisplayNameFromUserMetadata(context.Principal!, identity);

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy.RequireRole("admin"));

        return services;
    }

    private static void ExtractRolesFromAppMetadata(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var appMetadata = principal.FindFirstValue("app_metadata");
        if (appMetadata == null)
            return;

        using var doc = JsonDocument.Parse(appMetadata);

        if (doc.RootElement.TryGetProperty("roles", out var rolesProp) &&
            rolesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rolesProp.EnumerateArray())
            {
                var role = item.GetString();
                if (!string.IsNullOrEmpty(role))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("role", out var roleProp))
        {
            var role = roleProp.GetString();
            if (!string.IsNullOrEmpty(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }

    private static void ExtractDisplayNameFromUserMetadata(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var userMetadata = principal.FindFirstValue("user_metadata");
        if (userMetadata == null)
            return;

        using var doc = JsonDocument.Parse(userMetadata);

        if (doc.RootElement.TryGetProperty("full_name", out var fullName))
        {
            var name = fullName.GetString();
            if (!string.IsNullOrEmpty(name))
            {
                identity.AddClaim(new Claim("display_name", name));
            }
        }
    }

    private static bool IsMissingSigningKeyFailure(Exception exception)
    {
        return exception is SecurityTokenSignatureKeyNotFoundException;
    }
}
