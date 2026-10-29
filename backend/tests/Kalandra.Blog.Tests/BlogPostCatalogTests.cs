namespace Kalandra.Blog.Tests;

public class BlogPostCatalogTests
{
    private static readonly IBlogPostCatalog Catalog = new BlogPostCatalog();

    [Fact]
    public void Find_ReturnsThePost_ForAPublishedSlug() =>
        Assert.NotNull(Catalog.Find("zero-code-validations-in-your-dotnet-api"));

    [Fact]
    public void Find_ReturnsNull_ForAnUnpublishedSlug() =>
        Assert.Null(Catalog.Find("not-a-real-post"));

    [Fact]
    public void CommentStreams_AreDistinctPerPost()
    {
        // They share one global Marten id namespace, so a copy-paste that collides two posts'
        // comment streams would cross-contaminate them.
        var zeroCode = Catalog.Find("zero-code-validations-in-your-dotnet-api")!;
        var helloWorld = Catalog.Find("hello-world")!;
        Assert.Distinct([zeroCode.CommentsStreamId, helloWorld.CommentsStreamId]);
    }
}
