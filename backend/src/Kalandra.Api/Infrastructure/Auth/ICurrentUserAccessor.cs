namespace Kalandra.Api.Infrastructure.Auth;

public interface ICurrentUserAccessor
{
    CurrentUser CurrentUser { get; }
}
