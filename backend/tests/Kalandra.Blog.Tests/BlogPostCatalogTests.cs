namespace Kalandra.Blog.Tests;

public class BlogPostCatalogTests
{
    private static readonly IBlogPostCatalog Catalog = new BlogPostCatalog();

    private static BlogPostSlug Slug(string value) => BlogPostSlug.TryCreate(value)!.Value;

    [Fact]
    public void IsKnown_TrueForAPublishedPost() =>
        Assert.True(Catalog.IsKnown(Slug("zero-code-validations-in-your-dotnet-api")));

    [Fact]
    public void IsKnown_FalseForAWellShapedButUnpublishedSlug() =>
        Assert.False(Catalog.IsKnown(Slug("not-a-real-post")));
}
