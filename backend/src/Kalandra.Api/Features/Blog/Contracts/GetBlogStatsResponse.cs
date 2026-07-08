using Kalandra.Blog.Queries;

namespace Kalandra.Api.Features.Blog.Contracts;

public record BlogPostStatsResponse(string Slug, int TotalReads, int TotalReactions, int? ViewerReads);

public record GetBlogStatsResponse(IReadOnlyList<BlogPostStatsResponse> Posts)
{
    public static GetBlogStatsResponse Serialize(IReadOnlyList<BlogPostStats> stats) => new(
        [.. stats.Select(s => new BlogPostStatsResponse(s.Slug, s.TotalReads, s.TotalReactions, s.ViewerReads))]);
}
