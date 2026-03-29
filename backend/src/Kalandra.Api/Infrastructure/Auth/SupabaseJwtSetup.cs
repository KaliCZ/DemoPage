using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        // HMAC key for HS256 tokens (production Supabase)
        var signingKeys = new List<SecurityKey>();
        if (!string.IsNullOrEmpty(authOptions.SupabaseJwtSecret))
        {
            signingKeys.Add(new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(authOptions.SupabaseJwtSecret)));
        }

        // JWKS keys for ES256 tokens (newer Supabase uses ECDSA)
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var jwksJson = httpClient.GetStringAsync(
                $"{projectUrl}/auth/v1/.well-known/jwks.json").GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(jwksJson);
            signingKeys.AddRange(jwks.GetSigningKeys());
        }
        catch
        {
            // JWKS endpoint not available — HMAC key alone will be used
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeys,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy =>
                policy.RequireAssertion(context =>
                {
                    var userId = context.User.GetUserId();
                    return userId != null && authOptions.AdminUserIds.Contains(userId);
                }));

        return services;
    }
}
