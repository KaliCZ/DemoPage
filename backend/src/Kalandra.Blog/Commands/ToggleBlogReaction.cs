using Kalandra.Blog.Entities;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record ToggleBlogReactionCommand(string Slug, Guid VisitorId, CurrentUser? User, BlogReactionKind Kind);

public class ToggleBlogReactionHandler(IDocumentSession session)
{
    /// <summary>Rows are keyed by reactor, so concurrent reactions from different people never conflict.</summary>
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
