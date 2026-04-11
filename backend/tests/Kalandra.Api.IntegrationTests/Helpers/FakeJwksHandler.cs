using System.Net;
using System.Text;
using System.Text.Json;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>
/// An HttpMessageHandler that serves fake OpenID Connect discovery and JWKS
/// endpoints, so the standard JWKS validation path works in tests without
/// a running Supabase instance.
/// </summary>
public class FakeJwksHandler : HttpMessageHandler
{
    private readonly string _issuer;

    public FakeJwksHandler(string issuer)
    {
        _issuer = issuer.TrimEnd('/');
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        if (path.EndsWith("/.well-known/openid-configuration"))
        {
            return Task.FromResult(JsonResponse(new
            {
                issuer = _issuer,
                jwks_uri = $"{_issuer}/.well-known/jwks.json",
                id_token_signing_alg_values_supported = new[] { "ES256" }
            }));
        }

        if (path.EndsWith("/.well-known/jwks.json"))
        {
            var jwk = JwtTestHelper.PublicJwk;
            return Task.FromResult(JsonResponse(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = jwk.Kty,
                        crv = jwk.Crv,
                        x = jwk.X,
                        y = jwk.Y,
                        use = jwk.Use,
                        alg = jwk.Alg,
                        kid = jwk.Kid
                    }
                }
            }));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage JsonResponse(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };
    }
}
