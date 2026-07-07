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
    public static GetBlogReactionsResponse Serialize(BlogPostReactions reactions, Guid? viewerId) => new(
        Counts: new BlogReactionCountsResponse(
            ThumbsUp: reactions.CountOf(BlogReactionKind.ThumbsUp),
            ThumbsDown: reactions.CountOf(BlogReactionKind.ThumbsDown),
            Heart: reactions.CountOf(BlogReactionKind.Heart),
            Insightful: reactions.CountOf(BlogReactionKind.Insightful),
            Rocket: reactions.CountOf(BlogReactionKind.Rocket)),
        Mine: viewerId is { } id ? [.. reactions.KindsOf(id)] : []);
}
