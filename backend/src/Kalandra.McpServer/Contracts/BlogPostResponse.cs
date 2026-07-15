using Kalandra.Blog.Feed;
using Kalandra.Blog.Queries;

namespace Kalandra.McpServer.Contracts;

/// <summary>
/// A feed post merged with the same per-post totals the blog index shows. Viewer fields are null for
/// anonymous callers; stats default to zero when the feed briefly leads the backend's post catalog.
/// </summary>
public record BlogPostResponse(
    string Slug,
    string Title,
    string Summary,
    string Link,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags,
    int TotalViews,
    int UniqueVisitors,
    int TotalReactions,
    int TotalComments,
    int? ViewerViews,
    bool? Watched)
{
    public static BlogPostResponse Serialize(BlogPostSummary post, BlogPostStats? stats) => new(
        Slug: post.Slug,
        Title: post.Title,
        Summary: post.Summary,
        Link: post.Link,
        PublishedAt: post.PublishedAt,
        Tags: post.Tags,
        TotalViews: stats?.TotalViews ?? 0,
        UniqueVisitors: stats?.UniqueVisitors ?? 0,
        TotalReactions: stats?.TotalReactions ?? 0,
        TotalComments: stats?.TotalComments ?? 0,
        ViewerViews: stats?.ViewerViews,
        Watched: stats?.ViewerViews is { } viewerViews ? viewerViews > 0 : null);
}
