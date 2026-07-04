namespace Kalandra.Blog.Tests;

public class BlogPostSlugTests
{
    [Theory]
    [InlineData("post")]
    [InlineData("zero-code-validations-in-your-dotnet-api")]
    [InlineData("a1-b2-c3")]
    [InlineData("2026")]
    public void TryCreate_ValidSlug_ReturnsValue(string slug)
    {
        Assert.Equal(slug, BlogPostSlug.TryCreate(slug)?.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Uppercase-Slug")]
    [InlineData("with spaces")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    [InlineData("diakritika-č")]
    [InlineData("path/traversal")]
    public void TryCreate_InvalidSlug_ReturnsNull(string? slug)
    {
        Assert.Null(BlogPostSlug.TryCreate(slug));
    }

    [Fact]
    public void TryCreate_OverMaxLength_ReturnsNull()
    {
        var slug = new string('a', BlogPostSlug.MaxLength + 1);
        Assert.Null(BlogPostSlug.TryCreate(slug));

        var atLimit = new string('a', BlogPostSlug.MaxLength);
        Assert.NotNull(BlogPostSlug.TryCreate(atLimit));
    }
}
