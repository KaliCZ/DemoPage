using Kalandra.Blog.Entities;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record ToggleBlogReactionCommand(string Slug, Guid VisitorId, CurrentUser? User, BlogReactionKind Kind);

public class ToggleBlogReactionHandler(IDocumentSession session)
{
    /// <summary>
    /// Toggling has no failure mode — anyone may react, signed in or not. The reactor's row is
    /// keyed by their identity, so different reactors never touch the same row and concurrent
    /// reactions can't conflict.
    /// </summary>
    public async Task ToggleAndSave(ToggleBlogReactionCommand command, CancellationToken ct)
    {
        var reactorId = command.User?.Id ?? command.VisitorId;
        var id = BlogReaction.IdFor(command.Slug, reactorId, command.Kind);

        var existing = await session.LoadAsync<BlogReaction>(id, ct);
        if (existing is not null)
            session.Delete(existing);
        else
            session.Store(new BlogReaction
            {
                Id = id,
                Slug = command.Slug,
                VisitorId = command.VisitorId,
                UserId = command.User?.Id,
                Kind = command.Kind,
            });

        await session.SaveChangesAsync(ct);
    }
}
