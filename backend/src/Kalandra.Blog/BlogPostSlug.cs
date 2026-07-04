using System.Text.RegularExpressions;

namespace Kalandra.Blog;

/// <summary>
/// Validated blog post slug. Slugs arrive from public URLs and mint event streams,
/// and the backend has no list of real posts (they live in the frontend repo) —
/// this shape gate is what stops junk URLs from creating streams.
/// </summary>
public readonly partial record struct BlogPostSlug
{
    public const int MaxLength = 120;

    public string Value { get; }

    private BlogPostSlug(string value) => Value = value;

    public static BlogPostSlug? TryCreate(string? slug) =>
        slug != null && slug.Length <= MaxLength && Shape().IsMatch(slug) ? new BlogPostSlug(slug) : null;

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Shape();
}
