using Kalandra.Api.Features.Blog.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Queries;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Features.Blog;

[ApiController]
[Route("api/blog/{slug}")]
[Produces("application/json")]
[Authorize]
public class BlogController(
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    IBlogPostCatalog postCatalog,
    ToggleBlogReactionHandler toggleReactionHandler,
    PostBlogCommentHandler postCommentHandler,
    DeleteBlogCommentHandler deleteCommentHandler,
    RecordBlogPostViewHandler recordViewHandler,
    LinkVisitorHandler linkVisitorHandler,
    GetBlogReactionsHandler getReactionsHandler,
    GetBlogCommentsHandler getCommentsHandler,
    GetBlogPostStatsHandler getStatsHandler) : ControllerBase
{
    private CurrentUser AppUser => currentUser.RequiredUser;

    // The anonymous visitor id the client mints and stores locally; keys views and reactions before sign-in.
    private bool TryGetVisitorId(out Guid visitorId) =>
        Guid.TryParse(Request.Headers["X-Visitor-Id"].ToString(), out visitorId);

    private ActionResult MissingVisitorId()
    {
        ModelState.AddModelError("visitorId", "Required");
        return ValidationProblem();
    }

    // ───── Reactions ─────

    [HttpGet("reactions")]
    [AllowAnonymous]
    [ProducesResponseType<GetBlogReactionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetBlogReactionsResponse>> GetReactions(string slug, CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();

        var reactions = await getReactionsHandler.Get(new GetBlogReactionsQuery(post.ReactionsStreamId), ct);
        var visitorId = TryGetVisitorId(out var vid) ? vid : (Guid?)null;
        return GetBlogReactionsResponse.Serialize(reactions, visitorId, currentUser.User?.Id);
    }

    [HttpPost("reactions/toggle")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType<GetBlogReactionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetBlogReactionsResponse>> ToggleReaction(
        string slug,
        [FromBody] ToggleBlogReactionRequest request,
        CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();
        if (!TryGetVisitorId(out var visitorId))
            return MissingVisitorId();

        var command = new ToggleBlogReactionCommand(
            ReactionsStreamId: post.ReactionsStreamId,
            VisitorId: visitorId,
            User: currentUser.User,
            Kind: request.Kind,
            Timestamp: timeProvider.GetUtcNow());

        var reactions = await toggleReactionHandler.ToggleAndSave(command, ct);
        return GetBlogReactionsResponse.Serialize(reactions, visitorId, currentUser.User?.Id);
    }

    // ───── Views ─────

    [HttpPost("views")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType<RecordBlogPostViewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordBlogPostViewResponse>> RecordView(
        string slug,
        // Temporary: backdates a view so pre-rollout Cloudflare traffic can be seeded past the
        // 15-minute window. Removed in the follow-up PR once the historical data is in place.
        [FromQuery] DateTimeOffset? at,
        CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();
        if (!TryGetVisitorId(out var visitorId))
            return MissingVisitorId();

        var command = new RecordBlogPostViewCommand(
            Slug: post.Slug,
            VisitorId: visitorId,
            UserId: currentUser.User?.Id,
            NowUtc: at ?? timeProvider.GetUtcNow());

        var result = await recordViewHandler.RecordAndSave(command, ct);
        return new RecordBlogPostViewResponse(result.PreviousViewCount, result.TotalViews, result.UniqueVisitors);
    }

    // ───── Stats ─────

    /// <summary>Batch stats for the blog index; the absolute route sidesteps the class-level {slug} template.</summary>
    [HttpGet("/api/blog/stats")]
    [AllowAnonymous]
    [ProducesResponseType<GetBlogStatsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetBlogStatsResponse>> GetStats(
        [FromQuery(Name = "slug")] string[] slugs,
        CancellationToken ct)
    {
        // Unknown slugs are dropped, not errors — the frontend owns the post list and may briefly lead the catalog.
        var posts = slugs.Distinct(StringComparer.Ordinal).Select(postCatalog.Find).OfType<BlogPost>().ToArray();

        var stats = await getStatsHandler.List(new GetBlogPostStatsQuery(posts, currentUser.User?.Id), ct);
        return GetBlogStatsResponse.Serialize(stats);
    }

    // ───── Visitor link ─────

    /// <summary>On sign-in, attributes the visitor's anonymous views and reactions to their account.</summary>
    [HttpPost("/api/blog/visitor/link")]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> LinkVisitor(CancellationToken ct)
    {
        if (!TryGetVisitorId(out var visitorId))
            return MissingVisitorId();

        await linkVisitorHandler.LinkAndSave(new LinkVisitorCommand(visitorId, AppUser.Id, timeProvider.GetUtcNow()), ct);
        return NoContent();
    }

    // ───── Comments ─────

    [HttpGet("comments")]
    [AllowAnonymous]
    [ProducesResponseType<ListBlogCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListBlogCommentsResponse>> GetComments(string slug, CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();

        var comments = await getCommentsHandler.GetForDisplay(new GetBlogCommentsQuery(post.CommentsStreamId), ct);
        return ListBlogCommentsResponse.Serialize(comments);
    }

    [HttpPost("comments")]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType<BlogCommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlogCommentResponse>> PostComment(
        string slug,
        [FromBody] PostBlogCommentRequest request,
        CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();

        // Client-supplied so an accidental resend reuses it and the workflow dedupes to one
        // comment; we mint one only when a caller omits it.
        var commentId = request.CommentId is { } id && id != Guid.Empty ? id : Guid.NewGuid();

        var comment = new BlogCommentPosted(
            CommentId: commentId,
            ParentCommentId: request.ParentCommentId,
            UserId: AppUser.Id,
            UserEmail: AppUser.Email,
            AuthorDisplayName: AppUser.FullName,
            AuthorAvatarUrl: AppUser.AvatarUrl,
            Content: request.Content,
            Timestamp: timeProvider.GetUtcNow());

        var result = await postCommentHandler.Post(new PostBlogCommentCommand(post, comment), ct);

        if (result.Error is { } error)
        {
            return error switch
            {
                PostBlogCommentError.ParentCommentNotFound => this.ValidationError("parentCommentId", PostCommentError.ParentCommentNotFound),
                PostBlogCommentError.ParentCommentDeleted => this.ValidationError("parentCommentId", PostCommentError.ParentCommentDeleted),
            };
        }

        return BlogCommentResponse.Serialize(result.Success!);
    }

    [HttpDelete("comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteComment(string slug, Guid commentId, CancellationToken ct)
    {
        if (postCatalog.Find(slug) is not { } post)
            return NotFound();

        var command = new DeleteBlogCommentCommand(
            CommentsStreamId: post.CommentsStreamId,
            CommentId: commentId,
            User: AppUser,
            Timestamp: timeProvider.GetUtcNow());

        var result = await deleteCommentHandler.DeleteAndSave(command, ct);

        if (result.Error is { } error)
        {
            return error switch
            {
                DeleteBlogCommentError.CommentNotFound => NotFound(),
                DeleteBlogCommentError.NotAuthorized => Forbid(),
                DeleteBlogCommentError.AlreadyDeleted => this.ValidationError("commentId", DeleteCommentError.AlreadyDeleted),
            };
        }

        return NoContent();
    }
}
