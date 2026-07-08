namespace Kalandra.Api.Features.Blog.Contracts;

/// <summary>The reader's count before this read — zero means the page shows "not read yet".</summary>
public record RecordBlogPostReadResponse(int PreviousReadCount);
