namespace Kalandra.Api.Infrastructure.Auth;

public interface ICurrentUserAccessor
{
    /// <summary>
    /// The authenticated user for the current request, or null if the
    /// request is anonymous.
    /// </summary>
    CurrentUser? User { get; }

    /// <summary>
    /// The authenticated user for the current request. Throws if the
    /// request is anonymous — use from endpoints that are [Authorize]d.
    /// </summary>
    CurrentUser RequiredUser { get; }
}
