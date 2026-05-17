using Kalandra.Blog.Commands;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Queries;

namespace Kalandra.Api.Features.Blog.Contracts;

public record BlogReactionCount(BlogReactionEmoji Emoji, int Count);

public record BlogReactionsResponse(
    IReadOnlyList<BlogReactionCount> Counts,
    IReadOnlyList<BlogReactionEmoji> UserReactions)
{
    public static BlogReactionsResponse Serialize(BlogReactionsView view)
    {
        // Always emit one row per supported emoji so the client can render the
        // bar in a stable order without locale-dependent dictionary iteration.
        var counts = Enum.GetValues<BlogReactionEmoji>()
            .Select(e => new BlogReactionCount(e, view.Counts.GetValueOrDefault(e, 0)))
            .ToList();
        return new BlogReactionsResponse(counts, view.UserReactions);
    }
}

public record ToggleBlogReactionResponse(BlogReactionEmoji Emoji, BlogReactionAction Action);
