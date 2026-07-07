namespace Kalandra.Blog;

/// <summary>A published post's stable identity: its slug and the Guids of its comment and reaction event streams.</summary>
public sealed record BlogPost(BlogPostSlug Slug, Guid CommentsStreamId, Guid ReactionsStreamId);

/// <summary>
/// The backend's copy of the published blog posts (the posts themselves live in the
/// frontend). A monorepo lets the backend gate reactions/comments to real posts and
/// pin each post's stream Guids here rather than deriving them from the slug, so a
/// slug rename can't orphan its data. The all-posts reaction E2E fails if this list
/// drifts from the frontend.
/// </summary>
public interface IBlogPostCatalog
{
    BlogPost? Find(BlogPostSlug slug);
}

public sealed class BlogPostCatalog : IBlogPostCatalog
{
    // Stream Guids are hand-assigned and permanent: they key the event streams, so they must
    // never change once a post has data, and a post's two streams must stay distinct because
    // Marten keeps every stream in one global id namespace.
    private static readonly IReadOnlyDictionary<string, BlogPost> Posts =
        new[]
        {
            new BlogPost(
                Slug: BlogPostSlug.TryCreate("zero-code-validations-in-your-dotnet-api")!.Value,
                CommentsStreamId: Guid.Parse("b1090001-0000-4000-8000-0000000000c0"),
                ReactionsStreamId: Guid.Parse("b1090001-0000-4000-8000-0000000000e0")),
        }.ToDictionary(post => post.Slug.Value, StringComparer.Ordinal);

    public BlogPost? Find(BlogPostSlug slug) => Posts.GetValueOrDefault(slug.Value);
}
