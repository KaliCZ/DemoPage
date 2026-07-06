namespace Kalandra.Blog;

/// <summary>
/// The backend's copy of the published blog slugs. The posts themselves live in
/// the frontend (pages/[...lang]/blog/*.astro); a monorepo lets the backend gate
/// reactions and comments to slugs that are real posts, rather than any
/// well-shaped URL. The all-posts reaction E2E fails if the two lists drift apart.
/// </summary>
public interface IBlogPostCatalog
{
    bool IsKnown(BlogPostSlug slug);
}

public sealed class BlogPostCatalog : IBlogPostCatalog
{
    private static readonly HashSet<string> Slugs = new(StringComparer.Ordinal)
    {
        "zero-code-validations-in-your-dotnet-api",
    };

    public bool IsKnown(BlogPostSlug slug) => Slugs.Contains(slug.Value);
}
