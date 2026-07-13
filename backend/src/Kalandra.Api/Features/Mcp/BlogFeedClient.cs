using System.Globalization;
using System.Xml.Linq;

namespace Kalandra.Api.Features.Mcp;

public record BlogPostSummary(
    string Slug,
    string Title,
    string Summary,
    string Link,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags)
{
    // Null for anonymous callers, who have no reading history; the tool fills these in when signed in.
    public int? ViewerViews { get; init; }
    public bool? Watched { get; init; }
}

/// <summary>
/// Reads the published posts from the site's own RSS feed. The backend's post catalog holds
/// only slugs and stream ids, so the feed the frontend generates is the source of post titles,
/// summaries, and tags for the MCP <c>list_blog_posts</c> tool.
/// </summary>
public sealed class BlogFeedClient(HttpClient httpClient, BlogFeedConfig config)
{
    public async Task<IReadOnlyList<BlogPostSummary>> ListPosts(CancellationToken ct)
    {
        var xml = await httpClient.GetStringAsync(config.RssUrl, ct);
        return Parse(xml);
    }

    public static IReadOnlyList<BlogPostSummary> Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants("item")
            .Select(item =>
            {
                var link = item.Element("link")?.Value.Trim() ?? "";
                return new BlogPostSummary(
                    Slug: SlugFromLink(link),
                    Title: item.Element("title")?.Value.Trim() ?? "",
                    Summary: item.Element("description")?.Value.Trim() ?? "",
                    Link: link,
                    PublishedAt: DateTimeOffset.TryParse(
                        item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var pubDate)
                        ? pubDate
                        : null,
                    Tags: [.. item.Elements("category").Select(category => category.Value.Trim())]);
            })
            .ToList();
    }

    // Items link to the post page (…/blog/{slug}, possibly locale-prefixed); the trailing
    // segment is the slug the comment endpoints key on.
    private static string SlugFromLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
            ? uri.Segments[^1].Trim('/')
            : link.TrimEnd('/').Split('/')[^1];
}
