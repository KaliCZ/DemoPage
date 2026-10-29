using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Commands;

public record LinkVisitorCommand(Guid VisitorId, Guid UserId);

public class LinkVisitorHandler(IDocumentSession session)
{
    public async Task LinkAndSave(LinkVisitorCommand command, CancellationToken ct)
    {
        var anonymousReactions = await session.Query<BlogReaction>()
            .Where(reaction => reaction.VisitorId == command.VisitorId && reaction.UserId == null)
            .ToListAsync(ct);
        foreach (var reaction in anonymousReactions)
        {
            session.Delete(reaction);
            // If the account already reacted with this kind, the upsert dedupes to one row.
            session.Store(new BlogReaction
            {
                Id = BlogReaction.IdFor(reaction.Slug, command.UserId, reaction.Kind),
                Slug = reaction.Slug,
                VisitorId = reaction.VisitorId,
                UserId = command.UserId,
                Kind = reaction.Kind,
            });
        }

        var views = await session.Query<BlogPostVisitorView>()
            .Where(view => view.VisitorId == command.VisitorId && view.UserId == null)
            .ToListAsync(ct);
        foreach (var view in views)
        {
            view.UserId = command.UserId;
            session.Store(view);
        }

        await session.SaveChangesAsync(ct);
    }
}
