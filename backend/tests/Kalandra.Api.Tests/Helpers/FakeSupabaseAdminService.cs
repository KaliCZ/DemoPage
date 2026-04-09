using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Tests.Helpers;

public class FakeSupabaseAdminService : ISupabaseAdminService
{
    public ChangePasswordError? NextChangePasswordResult { get; set; }
    public (CurrentUser User, string Password)? LastChangePasswordCall { get; private set; }

    public Task<ChangePasswordError?> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct)
    {
        LastChangePasswordCall = (user, password);
        return Task.FromResult(NextChangePasswordResult);
    }
}
