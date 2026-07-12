using Kalandra.Blog.Entities;
using Kalandra.Blog.Queries;

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
    public static GetBlogReactionsResponse Serialize(BlogReactionSummary summary) =>
        new(
            Counts: new BlogReactionCountsResponse(
                ThumbsUp: summary.CountOf(BlogReactionKind.ThumbsUp),
                ThumbsDown: summary.CountOf(BlogReactionKind.ThumbsDown),
                Heart: summary.CountOf(BlogReactionKind.Heart),
                Insightful: summary.CountOf(BlogReactionKind.Insightful),
                Rocket: summary.CountOf(BlogReactionKind.Rocket)),
            Mine: [.. summary.Mine]);
}
