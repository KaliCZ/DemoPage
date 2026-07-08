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
    public void CommentReactionAndReadStreams_AreDistinct()
    {
        // They share one global Marten id namespace, so a copy-paste that collides them
        // would cross-contaminate a post's comments, reactions, and reads.
        var post = Catalog.Find("zero-code-validations-in-your-dotnet-api")!;
        Assert.Distinct([post.CommentsStreamId, post.ReactionsStreamId, post.ReadsStreamId]);
    }
}
