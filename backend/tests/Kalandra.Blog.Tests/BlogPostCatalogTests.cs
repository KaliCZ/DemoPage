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
        // Stream ids share one global Marten namespace, so a copy-pasted id would merge two posts' comments.
        var zeroCode = Catalog.Find("zero-code-validations-in-your-dotnet-api")!;
        var helloWorld = Catalog.Find("hello-world")!;
        Assert.Distinct([zeroCode.CommentsStreamId, helloWorld.CommentsStreamId]);
    }
}
