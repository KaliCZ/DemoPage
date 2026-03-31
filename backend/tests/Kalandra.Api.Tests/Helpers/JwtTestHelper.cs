using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kalandra.Api.Tests.Helpers;

public static class JwtTestHelper
{
    public const string TestSecret = "super-secret-test-jwt-key-that-is-at-least-32-chars-long!!";
    public const string TestIssuer = "https://test-project.supabase.co/auth/v1";
    public const string TestAudience = "authenticated";

    public static string GenerateToken(
        string userId = "test-user-id",
        string email = "test@example.com",
        bool isAdmin = false)
    {
        var appMetadata = isAdmin
            ? """{"provider":"email","providers":["email"],"role":"admin"}"""
            : """{"provider":"email","providers":["email"]}""";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Aud, TestAudience),
            new("app_metadata", appMetadata, JsonClaimValueTypes.Json),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
