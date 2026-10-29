namespace Kalandra.Infrastructure.Auth;

public interface ICurrentUserAccessor
{
    CurrentUser? User { get; }

    /// <summary>Throws if the request is anonymous.</summary>
    CurrentUser RequiredUser { get; }
}
