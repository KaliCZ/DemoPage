using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record ToggleBlogReactionCommand(
    Guid ReactionsStreamId,
    Guid VisitorId,
    CurrentUser? User,
    BlogReactionKind Kind,
    DateTimeOffset Timestamp);

public class ToggleBlogReactionHandler(IDocumentSession session)
{
    /// <summary>
    /// Toggling has no failure mode — anyone may react, signed in or not — so this
    /// returns the updated state directly instead of a Result.
    /// </summary>
    public async Task<BlogPostReactions> ToggleAndSave(ToggleBlogReactionCommand command, CancellationToken ct)
    {
        var streamId = command.ReactionsStreamId;
        var reactions = await session.Events.AggregateStreamAsync<BlogPostReactions>(streamId, token: ct)
            ?? new BlogPostReactions();

        var userId = command.User?.Id;
        var reactionEvent = reactions.Toggle(command.VisitorId, userId, command.Kind, command.Timestamp);
        session.Events.Append(streamId, reactionEvent);

        // Index anonymous reactions by visitor so sign-in can attribute just these streams (see LinkVisitor).
        if (userId is null)
        {
            var index = await session.LoadAsync<VisitorReactions>(command.VisitorId, ct)
                ?? new VisitorReactions { Id = command.VisitorId };
            index.ReactionStreamIds.Add(streamId);
            session.Store(index);
        }

        await session.SaveChangesAsync(ct);

        switch (reactionEvent)
        {
            case BlogReactionAdded added:
                reactions.Apply(added);
                break;
            case BlogReactionRemoved removed:
                reactions.Apply(removed);
                break;
        }

        return reactions;
    }
}
