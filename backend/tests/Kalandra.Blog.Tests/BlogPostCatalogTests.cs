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
    public void CommentAndReactionStreams_AreDistinct()
    {
        // They share one global Marten id namespace, so a copy-paste that collides them
        // would cross-contaminate a post's comments and reactions.
        var post = Catalog.Find("zero-code-validations-in-your-dotnet-api")!;
        Assert.NotEqual(post.CommentsStreamId, post.ReactionsStreamId);
    }
}
