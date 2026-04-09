using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Tests.Helpers;

public class FakeSupabaseAdminService : ISupabaseAdminService
{
    public bool NextCallSucceeds { get; set; } = true;
    public string? NextCallError { get; set; }
    public (CurrentUser User, string Password)? LastChangePasswordCall { get; private set; }

    public Task<SupabaseAdminResult> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct)
    {
        LastChangePasswordCall = (user, password);

        return Task.FromResult(NextCallSucceeds
            ? new SupabaseAdminResult(Success: true)
            : new SupabaseAdminResult(Success: false, Error: NextCallError ?? "Simulated failure"));
    }
}
