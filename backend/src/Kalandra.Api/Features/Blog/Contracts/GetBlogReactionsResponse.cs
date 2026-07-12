using Kalandra.Blog.Entities;

namespace Kalandra.Api.Features.Blog.Contracts;

public record BlogReactionCountsResponse(
    int ThumbsUp,
    int ThumbsDown,
    int Heart,
    int Insightful,
    int Rocket);

public record GetBlogReactionsResponse(
    BlogReactionCountsResponse Counts,
    IReadOnlyList<BlogReactionKind> Mine)
{
    // "Mine" unions the anonymous visitor id with the signed-in user id so a reaction shows as
    // the viewer's whether or not sign-in attribution (LinkVisitor) has folded it into the account yet.
    public static GetBlogReactionsResponse Serialize(BlogPostReactions reactions, Guid? visitorId, Guid? userId)
    {
        var mine = new HashSet<BlogReactionKind>();
        if (visitorId is { } vid)
            mine.UnionWith(reactions.GetByVisitor(vid));
        if (userId is { } uid)
            mine.UnionWith(reactions.GetByUser(uid));

        return new(
            Counts: new BlogReactionCountsResponse(
                ThumbsUp: reactions.CountOf(BlogReactionKind.ThumbsUp),
                ThumbsDown: reactions.CountOf(BlogReactionKind.ThumbsDown),
                Heart: reactions.CountOf(BlogReactionKind.Heart),
                Insightful: reactions.CountOf(BlogReactionKind.Insightful),
                Rocket: reactions.CountOf(BlogReactionKind.Rocket)),
            Mine: [.. mine]);
    }
}
