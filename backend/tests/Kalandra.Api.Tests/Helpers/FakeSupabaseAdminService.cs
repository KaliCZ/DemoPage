using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Tests.Helpers;

public class FakeSupabaseAdminService : ISupabaseAdminService
{
    public bool NextCallSucceeds { get; set; } = true;
    public string? NextCallError { get; set; }
    public (string UserId, object Payload)? LastUpdateCall { get; private set; }

    public Task<SupabaseAdminResult> UpdateUserAsync(
        string userId,
        object updatePayload,
        CancellationToken ct)
    {
        LastUpdateCall = (userId, updatePayload);

        return Task.FromResult(NextCallSucceeds
            ? new SupabaseAdminResult(Success: true)
            : new SupabaseAdminResult(Success: false, Error: NextCallError ?? "Simulated failure"));
    }
}
