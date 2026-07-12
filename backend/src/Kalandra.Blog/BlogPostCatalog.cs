namespace Kalandra.Blog;

/// <summary>A published post's stable identity: its slug and the Guids of its comment and reaction event streams.</summary>
public sealed record BlogPost(string Slug, Guid CommentsStreamId, Guid ReactionsStreamId);

/// <summary>
/// The backend's copy of the published blog posts (the posts themselves live in the
/// frontend). A monorepo lets the backend gate reactions/comments to real posts and
/// pin each post's stream Guids here rather than deriving them from the slug, so a
/// slug rename can't orphan its data. The all-posts reaction E2E fails if this list
/// drifts from the frontend.
/// </summary>
public interface IBlogPostCatalog
{
    BlogPost? Find(string slug);
}

public sealed class BlogPostCatalog : IBlogPostCatalog
{
    // Hand-assigned and permanent: they key the event streams, so they can't change once a post has
    // data, and a post's ids must stay distinct (Marten keeps every stream in one id namespace).
    private static readonly IReadOnlyDictionary<string, BlogPost> Posts =
        new[]
        {
            new BlogPost(
                Slug: "zero-code-validations-in-your-dotnet-api",
                CommentsStreamId: Guid.Parse("b1090001-0000-4000-8000-0000000000c0"),
                ReactionsStreamId: Guid.Parse("b1090001-0000-4000-8000-0000000000e0")),
            new BlogPost(
                Slug: "hello-world",
                CommentsStreamId: Guid.Parse("b1090002-0000-4000-8000-0000000000c0"),
                ReactionsStreamId: Guid.Parse("b1090002-0000-4000-8000-0000000000e0")),
        }.ToDictionary(post => post.Slug, StringComparer.Ordinal);

    public BlogPost? Find(string slug) => Posts.GetValueOrDefault(slug);
}
