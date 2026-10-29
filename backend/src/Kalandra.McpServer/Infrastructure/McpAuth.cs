using System.Security.Claims;
using System.Text.Json;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace Kalandra.McpServer.Infrastructure;

public static class McpAuth
{
    public static void Add(IServiceCollection services, SupabaseConfig supabaseConfig, McpServerConfig mcpConfig)
    {
        var projectUrl = supabaseConfig.ProjectUrl.Value.TrimEnd('/');
        var issuer = $"{projectUrl}/auth/v1";
        var metadataAddress = $"{issuer}/.well-known/openid-configuration";
        var requireHttps = projectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication(options =>
            {
                // The MCP scheme challenges so the 401 carries the resource_metadata that points clients at Supabase.
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
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
                    // Supabase's `aud` for third-party OAuth tokens is unconfirmed; tighten to the RFC 8707 resource audience once it is.
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                            ExtractRolesFromAppMetadata(context.Principal, identity);
                        return Task.CompletedTask;
                    },
                };
            })
            .AddMcp(options =>
            {
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    // Canonical resource id — no trailing slash, per RFC 8707/9728.
                    Resource = mcpConfig.ResourceUri.ToString().TrimEnd('/'),
                    AuthorizationServers = { issuer },
                    ScopesSupported = { "openid", "email", "profile" },
                };
            });

        services.AddAuthorization();
    }

    public static void Use(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    /// <summary>
    /// Mirrors the API's role projection so the shared CurrentUserFactory reads the same roles on both hosts.
    /// </summary>
    private static void ExtractRolesFromAppMetadata(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var appMetadata = principal.FindFirst("app_metadata")?.Value;
        if (string.IsNullOrEmpty(appMetadata))
            return;

        using var doc = JsonDocument.Parse(appMetadata);
        if (doc.RootElement.TryGetProperty("roles", out var rolesProp) && rolesProp.ValueKind == JsonValueKind.Array)
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
