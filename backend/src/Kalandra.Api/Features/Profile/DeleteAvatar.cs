using Kalandra.Infrastructure.Avatars;

namespace Kalandra.Api.Features.Profile;

public class DeleteAvatarHandler(IAvatarService avatarService)
{
    public async Task HandleAsync(Guid userId, CancellationToken ct)
    {
        await avatarService.RemoveAvatarAsync(userId, ct);
    }
}
