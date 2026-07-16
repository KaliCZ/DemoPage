using System.ComponentModel;
using Kalandra.Blog;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Contracts;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Feed;
using Kalandra.Blog.Queries;
using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Queries;
using Kalandra.McpServer.Contracts;
using Kalandra.McpServer.Infrastructure;
using ModelContextProtocol.Server;

namespace Kalandra.McpServer.Tools;

/// <summary>
/// MCP tools for the blog and the user's own activity. Reads and writes go through the same
/// domain handlers the REST controllers use; the post list comes from the site's RSS feed.
/// Reading the blog is public; the class-level gate keeps everything else account-only by default.
/// </summary>
[McpServerToolType]
public sealed class BlogMcpTools(
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    IBlogPostCatalog postCatalog,
    BlogFeedClient blogFeedClient,
    GetBlogPostStatsHandler statsHandler,
    GetBlogCommentsHandler getCommentsHandler,
    PostBlogCommentHandler postCommentHandler,
    ListMyBlogCommentsHandler myBlogCommentsHandler,
    ListMyJobOfferCommentsHandler myJobOfferCommentsHandler)
{
    [McpServerTool(Name = "list_blog_posts")]
    [Description("List the published posts on the kalandra.tech blog (title, summary, slug, link, tags), each " +
                 "with the totals the blog index shows: views, unique visitors, reactions, and comments. " +
                 "Each link is a public web page with the full post — fetch it to read the content. " +
                 "For a signed-in user each post also reports viewerViews and watched so you can tell which " +
                 "ones they have already read. Use the slug with the blog comment tools.")]
    public async Task<IReadOnlyList<BlogPostResponse>> ListBlogPosts(CancellationToken ct = default)
    {
        var posts = await blogFeedClient.ListPosts(ct);

        // Slugs the catalog doesn't know yet keep zero stats instead of vanishing — the feed may briefly lead it.
        var catalogPosts = posts.Select(post => postCatalog.Find(post.Slug)).OfType<BlogPost>().ToArray();
        var stats = await statsHandler.List(new GetBlogPostStatsQuery(catalogPosts, currentUser.User?.Id), ct);
        var statsBySlug = stats.ToDictionary(stat => stat.Slug, StringComparer.Ordinal);

        return [.. posts.Select(post => BlogPostResponse.Serialize(post, statsBySlug.GetValueOrDefault(post.Slug)))];
    }

    [McpServerTool(Name = "get_blog_post_comments")]
    [Description("Read the public comment thread of a blog post. Replies reference their parent via parentCommentId.")]
    public async Task<ListBlogCommentsResponse> GetBlogPostComments(
        [Description("The post's slug, from list_blog_posts.")] string slug,
        CancellationToken ct = default)
    {
        if (postCatalog.Find(slug) is not { } post)
            throw new ToolRefusalException($"No blog post with slug '{slug}'.");

        var comments = await getCommentsHandler.GetForDisplay(new GetBlogCommentsQuery(post.CommentsStreamId), ct);
        return ListBlogCommentsResponse.Serialize(comments);
    }

    [McpServerTool(Name = "post_blog_comment")]
    [Description("[Authorized] Post a comment on a blog post as the user, optionally as a reply to an existing comment.")]
    public async Task<BlogCommentResponse> PostBlogComment(
        [Description("The post's slug, from list_blog_posts.")] string slug,
        [Description("The comment text.")] string content,
        [Description("Id of the comment being replied to; omit for a top-level comment.")] Guid? parentCommentId = null,
        CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);
        if (postCatalog.Find(slug) is not { } post)
            throw new ToolRefusalException($"No blog post with slug '{slug}'.");

        var comment = new BlogCommentPosted(
            CommentId: Guid.NewGuid(),
            ParentCommentId: parentCommentId,
            UserId: user.Id,
            UserEmail: user.Email,
            AuthorDisplayName: user.FullName,
            AuthorAvatarUrl: user.AvatarUrl,
            Content: McpToolHelpers.Required(content, nameof(content)),
            Timestamp: timeProvider.GetUtcNow());

        var result = await postCommentHandler.PostAndSave(new PostBlogCommentCommand(post, comment), ct);
        if (result.Error is { } error)
            throw new ToolRefusalException(error switch
            {
                PostBlogCommentError.ParentCommentNotFound => "The comment you're replying to doesn't exist.",
                PostBlogCommentError.ParentCommentDeleted => "The comment you're replying to was deleted.",
            });

        return BlogCommentResponse.Serialize(result.Success!);
    }

    [McpServerTool(Name = "get_my_comments")]
    [Description("[Authorized] Everything the user has said on kalandra.tech and what came back: their blog comments " +
                 "with the replies received, and their job-offer comments with the site owner's responses.")]
    public async Task<GetMyCommentsResponse> GetMyComments(CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);
        var blogComments = await myBlogCommentsHandler.List(new ListMyBlogCommentsQuery(user), ct);
        var jobOfferComments = await myJobOfferCommentsHandler.List(new ListMyJobOfferCommentsQuery(user), ct);
        return GetMyCommentsResponse.Serialize(blogComments, jobOfferComments);
    }
}
