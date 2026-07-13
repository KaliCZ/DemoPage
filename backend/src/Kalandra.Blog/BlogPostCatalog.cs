namespace Kalandra.Blog;

/// <summary>A published post's stable identity: its slug and the Guid of its comment event stream.</summary>
public sealed record BlogPost(string Slug, Guid CommentsStreamId);

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

    /// <summary>Reverse of <see cref="Find"/>: recovers the post from a comment event's stream id, which is all the notification subscription has.</summary>
    BlogPost? FindByCommentsStreamId(Guid commentsStreamId);

    /// <summary>Every published post — the set the stats snapshot refreshes.</summary>
    IReadOnlyCollection<BlogPost> All { get; }
}

public sealed class BlogPostCatalog : IBlogPostCatalog
{
    // Hand-assigned and permanent: it keys the comment event stream, so it can't change once a post
    // has comments (Marten keeps every stream in one id namespace).
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

    private static readonly IReadOnlyCollection<BlogPost> AllPosts = [.. Posts.Values];

    public IReadOnlyCollection<BlogPost> All => AllPosts;

    public BlogPost? Find(string slug) => Posts.GetValueOrDefault(slug);

    public BlogPost? FindByCommentsStreamId(Guid commentsStreamId) =>
        Posts.Values.FirstOrDefault(post => post.CommentsStreamId == commentsStreamId);
}
