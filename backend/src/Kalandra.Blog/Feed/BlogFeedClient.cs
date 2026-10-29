using System.Globalization;
using System.Xml.Linq;

namespace Kalandra.Blog.Feed;

public record BlogPostSummary(
    string Slug,
    string Title,
    string Summary,
    string Link,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags);

/// <summary>
/// The post catalog holds only slugs and stream ids, so the frontend's RSS feed is the source of titles, summaries and tags.
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

    // Links may be locale-prefixed, but the trailing segment is always the slug.
    private static string SlugFromLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
            ? uri.Segments[^1].Trim('/')
            : link.TrimEnd('/').Split('/')[^1];
}
