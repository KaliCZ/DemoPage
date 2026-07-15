using System.Security.Claims;
using System.Text.Json;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// The MCP server is an OAuth 2.0 resource server. Supabase is the authorization server: it runs the
/// OAuth flow and issues access tokens; this host only validates them and advertises where to get one.
/// </summary>
public static class McpAuth
{
    /// <summary>
    /// Anonymous callers pass (the public tools serve them), but a presented token must validate —
    /// an expired or bad token gets the OAuth challenge, not a silent downgrade to anonymous.
    /// </summary>
    public const string AnonymousOrValidTokenPolicy = "AnonymousOrValidToken";

    public static void Add(IServiceCollection services, SupabaseConfig supabaseConfig, McpServerConfig mcpConfig)
    {
        var projectUrl = supabaseConfig.ProjectUrl.Value.TrimEnd('/');
        var issuer = $"{projectUrl}/auth/v1";
        var metadataAddress = $"{issuer}/.well-known/openid-configuration";
        var requireHttps = projectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication(options =>
            {
                // JWT validates the bearer token; when it's absent or invalid the MCP scheme issues the
                // 401 + `WWW-Authenticate: resource_metadata=…` challenge that points clients at Supabase.
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
                    // Supabase's OAuth-server audience for third-party tokens isn't observable until the
                    // server is turned on; validate issuer + signature + lifetime now and tighten to an
                    // RFC 8707 resource-bound audience once we can confirm the token's `aud`.
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

        services.AddAuthorization(options =>
            options.AddPolicy(AnonymousOrValidTokenPolicy, policy => policy.RequireAssertion(context =>
                context.User.Identity?.IsAuthenticated == true
                || (context.Resource is HttpContext httpContext
                    && !httpContext.Request.Headers.ContainsKey(HeaderNames.Authorization)))));
    }

    public static void Use(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    /// <summary>
    /// Projects Supabase's app_metadata.roles into ClaimTypes.Role using canonical enum names, so the
    /// shared CurrentUserFactory reads the same roles here as in the API.
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
