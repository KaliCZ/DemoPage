namespace Kalandra.Api.Features.Blog.Contracts;

/// <summary>
/// PreviousViewCount is the reader's own view count before this visit — zero means "not read yet".
/// TotalViews (page views) and UniqueVisitors (distinct people) are the post's public totals.
/// </summary>
public record RecordBlogPostViewResponse(int PreviousViewCount, int TotalViews, int UniqueVisitors);
