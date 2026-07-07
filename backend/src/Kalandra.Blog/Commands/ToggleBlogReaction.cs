using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record ToggleBlogReactionCommand(
    Guid ReactionsStreamId,
    CurrentUser User,
    BlogReactionKind Kind,
    DateTimeOffset Timestamp);

public class ToggleBlogReactionHandler(IDocumentSession session)
{
    /// <summary>
    /// Toggling has no failure mode — any signed-in user may react — so this
    /// returns the updated state directly instead of a Result.
    /// </summary>
    public async Task<BlogPostReactions> ToggleAndSave(ToggleBlogReactionCommand command, CancellationToken ct)
    {
        var streamId = command.ReactionsStreamId;
        var reactions = await session.Events.AggregateStreamAsync<BlogPostReactions>(streamId, token: ct)
            ?? new BlogPostReactions();

        var reactionEvent = reactions.Toggle(command.User.Id, command.Kind, command.Timestamp);
        session.Events.Append(streamId, reactionEvent);
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
