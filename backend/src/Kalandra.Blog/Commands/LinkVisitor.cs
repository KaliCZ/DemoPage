using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Commands;

public record LinkVisitorCommand(Guid VisitorId, Guid UserId, DateTimeOffset Timestamp);

public class LinkVisitorHandler(IDocumentSession session)
{
    /// <summary>
    /// Folds a visitor's anonymous activity into their account on sign-in: attributes
    /// each reaction stream they touched while anonymous and stamps their view rows.
    /// </summary>
    public async Task LinkAndSave(LinkVisitorCommand command, CancellationToken ct)
    {
        var index = await session.LoadAsync<VisitorReactions>(command.VisitorId, ct);
        if (index is not null)
        {
            foreach (var streamId in index.ReactionStreamIds)
                session.Events.Append(streamId, new BlogReactorLinked(command.VisitorId, command.UserId, command.Timestamp));
            session.Delete(index);
        }

        var views = await session.Query<BlogPostVisitorView>()
            .Where(v => v.VisitorId == command.VisitorId && v.UserId == null)
            .ToListAsync(ct);
        foreach (var view in views)
        {
            view.UserId = command.UserId;
            session.Store(view);
        }

        await session.SaveChangesAsync(ct);
    }
}
