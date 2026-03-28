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

        // Extract project reference from URL: https://abcdef.supabase.co -> abcdef
        var projectUrl = authOptions.SupabaseProjectUrl.TrimEnd('/');
        var issuer = $"{projectUrl}/auth/v1";

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
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(authOptions.SupabaseJwtSecret)),
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
