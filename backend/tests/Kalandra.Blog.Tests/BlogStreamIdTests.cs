namespace Kalandra.Blog.Tests;

public class BlogStreamIdTests
{
    private static BlogPostSlug Slug(string value) => BlogPostSlug.TryCreate(value)!.Value;

    [Fact]
    public void SameSlug_AlwaysDerivesTheSameStreamIds()
    {
        Assert.Equal(BlogStreamId.ForComments(Slug("my-post")), BlogStreamId.ForComments(Slug("my-post")));
        Assert.Equal(BlogStreamId.ForReactions(Slug("my-post")), BlogStreamId.ForReactions(Slug("my-post")));
    }

    [Fact]
    public void CommentAndReactionStreams_NeverCollide()
    {
        Assert.NotEqual(BlogStreamId.ForComments(Slug("my-post")), BlogStreamId.ForReactions(Slug("my-post")));
    }

    [Fact]
    public void DifferentSlugs_DeriveDifferentStreamIds()
    {
        Assert.NotEqual(BlogStreamId.ForComments(Slug("post-one")), BlogStreamId.ForComments(Slug("post-two")));
        Assert.NotEqual(BlogStreamId.ForReactions(Slug("post-one")), BlogStreamId.ForReactions(Slug("post-two")));
    }
}
