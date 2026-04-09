using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Kalandra.Api.Tests.Helpers;

public static class JwtTestHelper
{
    public const string TestIssuer = "https://test-project.supabase.co/auth/v1";
    public const string TestAudience = "authenticated";

    private static readonly ECDsa Ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public static readonly ECDsaSecurityKey SigningKey = new(Ecdsa);
    public static readonly JsonWebKey PublicJwk = CreatePublicJwk();

    public static readonly Guid DefaultTestUserId = new("11111111-1111-1111-1111-111111111111");

    public static string GenerateToken(
        Guid? userId = null,
        string email = "test@example.com",
        bool isAdmin = false)
    {
        var sub = (userId ?? DefaultTestUserId).ToString();
        var appMetadata = isAdmin
            ? """{"provider":"email","providers":["email"],"roles":["admin"]}"""
            : """{"provider":"email","providers":["email"]}""";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, sub),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Aud, TestAudience),
            new("app_metadata", appMetadata, JsonClaimValueTypes.Json),
        };

        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static JsonWebKey CreatePublicJwk()
    {
        var parameters = Ecdsa.ExportParameters(includePrivateParameters: false);
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(parameters.Q.X!),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y!),
            Use = "sig",
            Alg = "ES256",
            Kid = "test-key-1"
        };
    }
}
