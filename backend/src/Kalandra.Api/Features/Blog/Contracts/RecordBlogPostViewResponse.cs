namespace Kalandra.Api.Features.Blog.Contracts;

/// <summary>PreviousViewCount is the reader's own count before this visit, so zero means "not read yet".</summary>
public record RecordBlogPostViewResponse(int PreviousViewCount, int TotalViews, int UniqueVisitors);
