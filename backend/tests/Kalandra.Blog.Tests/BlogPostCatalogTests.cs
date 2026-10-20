namespace Kalandra.Blog.Tests;

public class BlogPostCatalogTests
{
    private static readonly IBlogPostCatalog Catalog = new BlogPostCatalog();

    [Theory]
    [InlineData("zero-code-validations-in-your-dotnet-api")]
    [InlineData("type-safe-intervals-in-your-dotnet-api")]
    public void Find_ReturnsThePost_ForAPublishedSlug(string slug) =>
        Assert.NotNull(Catalog.Find(slug));

    [Fact]
    public void Find_ReturnsNull_ForAnUnpublishedSlug() =>
        Assert.Null(Catalog.Find("not-a-real-post"));

    [Fact]
    public void CommentStreams_AreDistinctPerPost()
    {
        // They share one global Marten id namespace, so a copy-paste that collides two posts'
        // comment streams would cross-contaminate them.
        var streamIds = new[] { "zero-code-validations-in-your-dotnet-api", "hello-world", "type-safe-intervals-in-your-dotnet-api" }
            .Select(slug => Catalog.Find(slug)!.CommentsStreamId);
        Assert.Distinct(streamIds);
    }
}
