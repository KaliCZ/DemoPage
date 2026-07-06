using Kalandra.Blog;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>
/// Tests mint a fresh random slug per case for stream isolation, so every
/// well-shaped slug counts as a real post here. The production catalog's actual
/// gating is covered by BlogPostCatalogTests and the all-posts reaction E2E.
/// </summary>
public sealed class OpenBlogPostCatalog : IBlogPostCatalog
{
    public bool IsKnown(BlogPostSlug slug) => true;
}
