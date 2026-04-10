using Kalandra.Infrastructure.Avatars;

namespace Kalandra.Api.Tests.Helpers;

public class NoOpAvatarService : IAvatarService
{
    public Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct) =>
        Task.FromResult(new Dictionary<Guid, Uri>());

    public Task<Uri> UploadAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct) =>
        Task.FromResult(new Uri($"https://test-project.supabase.co/storage/v1/object/public/avatars/{userId}/avatar.jpg"));

    public Task UpdateAvatarUrlAsync(Guid userId, Uri? avatarUrl, CancellationToken ct) =>
        Task.CompletedTask;

    public Task DeleteAvatarFilesAsync(Guid userId, CancellationToken ct) =>
        Task.CompletedTask;
}
