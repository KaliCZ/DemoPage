namespace Kalandra.Blog;

public sealed record BlogPost(string Slug, Guid CommentsStreamId);

/// <summary>
/// The backend's copy of the frontend's published posts, so reactions and comments are gated to real posts; the all-posts reaction E2E fails if it drifts.
/// </summary>
public interface IBlogPostCatalog
{
    BlogPost? Find(string slug);

    BlogPost? FindByCommentsStreamId(Guid commentsStreamId);

    IReadOnlyCollection<BlogPost> All { get; }
}

public sealed class BlogPostCatalog : IBlogPostCatalog
{
    // Hand-assigned so a slug rename can't orphan comments; never change one once its post has comments.
    private static readonly IReadOnlyDictionary<string, BlogPost> Posts =
        new[]
        {
            new BlogPost(
                Slug: "zero-code-validations-in-your-dotnet-api",
                CommentsStreamId: Guid.Parse("b1090001-0000-4000-8000-0000000000c0")),
            new BlogPost(
                Slug: "hello-world",
                CommentsStreamId: Guid.Parse("b1090002-0000-4000-8000-0000000000c0")),
        }.ToDictionary(post => post.Slug, StringComparer.Ordinal);

    public BlogPost? Find(string slug) => Posts.GetValueOrDefault(slug);

    public BlogPost? FindByCommentsStreamId(Guid commentsStreamId) =>
        Posts.Values.FirstOrDefault(post => post.CommentsStreamId == commentsStreamId);

    public IReadOnlyCollection<BlogPost> All { get; } = [.. Posts.Values];
}
