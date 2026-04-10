using Kalandra.Infrastructure.Avatars;

namespace Kalandra.Api.Features.Profile;

public class DeleteAvatarHandler(IAvatarService avatarService)
{
    public async Task HandleAsync(Guid userId, CancellationToken ct)
    {
        await avatarService.DeleteAvatarFilesAsync(userId, ct);
        await avatarService.UpdateAvatarUrlAsync(userId, avatarUrl: null, ct);
    }
}
