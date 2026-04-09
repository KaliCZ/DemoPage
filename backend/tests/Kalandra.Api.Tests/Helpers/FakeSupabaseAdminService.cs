using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Tests.Helpers;

public class FakeSupabaseAdminService : ISupabaseAdminService
{
    public ChangePasswordErrorCode? NextChangePasswordError { get; set; }
    public (CurrentUser User, string Password)? LastChangePasswordCall { get; private set; }

    public Task<ChangePasswordError?> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct)
    {
        LastChangePasswordCall = (user, password);
        var result = NextChangePasswordError is { } code
            ? new ChangePasswordError(code, "Fake error")
            : (ChangePasswordError?)null;
        return Task.FromResult(result);
    }
}
