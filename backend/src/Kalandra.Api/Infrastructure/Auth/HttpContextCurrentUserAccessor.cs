using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Kalandra.Api.Infrastructure.Auth;

public class HttpContextCurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private static readonly AsyncLocal<CurrentUser?> CachedUser = new();
    public CurrentUser? User => CachedUser.Value ??= BuildCurrentUser();
    public CurrentUser RequiredUser => User ?? throw new InvalidOperationException("No authenticated user on the current request.");

    private CurrentUser? BuildCurrentUser()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var userIdStr =
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var emailStr =
            principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Email);

        if (!Guid.TryParse(userIdStr, out var userId))
            return null;

        if (!MailAddress.TryCreate(emailStr, out var email))
            return null;

        var userMetadata = principal.FindFirstValue("user_metadata");
        var lazyDisplayName = new Lazy<string>(() => ExtractDisplayName(userMetadata, email));

        return new CurrentUser(
            Id: userId,
            Email: email,
            LazyDisplayName: lazyDisplayName,
            Roles: principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToImmutableArray()
        );
    }

    /// <summary>
    /// Streams user_metadata looking only for a "full_name" string, falling
    /// back to the email's local part. Utf8JsonReader avoids the JsonDocument
    /// tree allocation. Called at most once per request (via Lazy) and only
    /// when a caller actually reads DisplayName.
    /// </summary>
    private static string ExtractDisplayName(string? userMetadata, MailAddress email)
    {
        if (string.IsNullOrEmpty(userMetadata))
            return email.User;

        var bytes = Encoding.UTF8.GetBytes(userMetadata);
        var reader = new Utf8JsonReader(bytes);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return email.User;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (!reader.ValueTextEquals("full_name"u8))
            {
                reader.Read();
                reader.Skip();
                continue;
            }

            if (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                var name = reader.GetString();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            break;
        }

        return email.User;
    }
}
