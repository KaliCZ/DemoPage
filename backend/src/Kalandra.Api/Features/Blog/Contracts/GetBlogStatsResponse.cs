using Kalandra.Blog.Queries;

namespace Kalandra.Api.Features.Blog.Contracts;

public record BlogPostStatsResponse(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

public record GetBlogStatsResponse(IReadOnlyList<BlogPostStatsResponse> Posts)
{
    public static GetBlogStatsResponse Serialize(IReadOnlyList<BlogPostStats> stats) => new(
        [.. stats.Select(s => new BlogPostStatsResponse(s.Slug, s.TotalViews, s.UniqueVisitors, s.TotalReactions, s.TotalComments, s.ViewerViews))]);
}
