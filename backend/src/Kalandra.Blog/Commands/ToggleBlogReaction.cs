using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

/// <summary>
/// Click-to-toggle reaction. If the user has not reacted with this emoji on this
/// post, an add event is appended; otherwise a remove event is appended. The
/// stream replay decides which it is, so concurrent toggles can't accidentally
/// double-add or double-remove a reaction.
/// </summary>
public record ToggleBlogReactionCommand(
    NonEmptyString Slug,
    CurrentUser User,
    BlogReactionEmoji Emoji,
    DateTimeOffset Timestamp);

public enum BlogReactionAction { Added, Removed }

public record ToggleBlogReactionResult(BlogReactionAction Action, BlogReactionEmoji Emoji);

public class ToggleBlogReactionHandler(IDocumentSession session)
{
    public async Task<ToggleBlogReactionResult> HandleAsync(
        ToggleBlogReactionCommand command, CancellationToken ct)
    {
        var streamId = BlogStreamId.ForReactions(command.Slug);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);

        var hasReaction = false;
        foreach (var e in events)
        {
            switch (e.Data)
            {
                case BlogReactionAdded added when added.UserId == command.User.Id && added.Emoji == command.Emoji:
                    hasReaction = true;
                    break;
                case BlogReactionRemoved removed when removed.UserId == command.User.Id && removed.Emoji == command.Emoji:
                    hasReaction = false;
                    break;
            }
        }

        if (hasReaction)
        {
            var removed = new BlogReactionRemoved(
                Slug: command.Slug.Value,
                UserId: command.User.Id,
                Emoji: command.Emoji,
                Timestamp: command.Timestamp);
            session.Events.Append(streamId, removed);
            await session.SaveChangesAsync(ct);
            return new ToggleBlogReactionResult(BlogReactionAction.Removed, command.Emoji);
        }
        else
        {
            var added = new BlogReactionAdded(
                Slug: command.Slug.Value,
                UserId: command.User.Id,
                Emoji: command.Emoji,
                Timestamp: command.Timestamp);
            session.Events.Append(streamId, added);
            await session.SaveChangesAsync(ct);
            return new ToggleBlogReactionResult(BlogReactionAction.Added, command.Emoji);
        }
    }
}
