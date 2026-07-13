using Kalandra.Blog.Feed;

namespace Kalandra.Blog.Tests;

public class BlogFeedParseTests
{
    // Mirrors the shape frontend/src/pages/rss.xml.ts generates.
    private const string SampleFeed =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">
          <channel>
            <title>kalandra.tech — Blog</title>
            <item>
              <title>[EN] Zero-code validations in your .NET API</title>
              <link>https://www.kalandra.tech/blog/zero-code-validations-in-your-dotnet-api</link>
              <description>Let the type system do the validating.</description>
              <pubDate>Tue, 01 Jul 2025 00:00:00 GMT</pubDate>
              <category>dotnet</category>
              <category>validation</category>
            </item>
            <item>
              <title>[CS] Ahoj světe</title>
              <link>https://www.kalandra.tech/cs/blog/hello-world</link>
              <description>První příspěvek.</description>
              <pubDate>Mon, 02 Jun 2025 00:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void Parse_ExtractsTitleSummaryLinkDateAndTags()
    {
        var post = BlogFeedClient.Parse(SampleFeed)[0];

        Assert.Equal("[EN] Zero-code validations in your .NET API", post.Title);
        Assert.Equal("Let the type system do the validating.", post.Summary);
        Assert.Equal("https://www.kalandra.tech/blog/zero-code-validations-in-your-dotnet-api", post.Link);
        Assert.Equal(new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero), post.PublishedAt);
        Assert.Equal(["dotnet", "validation"], post.Tags);
    }

    [Fact]
    public void Parse_TakesSlugFromLastLinkSegment_EvenWhenLocalePrefixed()
    {
        var posts = BlogFeedClient.Parse(SampleFeed);

        Assert.Equal("zero-code-validations-in-your-dotnet-api", posts[0].Slug);
        Assert.Equal("hello-world", posts[1].Slug);
    }

    [Fact]
    public void Parse_ItemWithoutCategories_HasNoTags() =>
        Assert.Empty(BlogFeedClient.Parse(SampleFeed)[1].Tags);

    [Fact]
    public void Parse_EmptyChannel_ReturnsNoPosts() =>
        Assert.Empty(BlogFeedClient.Parse("""<?xml version="1.0"?><rss><channel><title>x</title></channel></rss>"""));
}
