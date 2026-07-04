using Kalandra.Api.Features.Blog.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Entities;
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
    ToggleBlogReactionHandler toggleReactionHandler,
    PostBlogCommentHandler postCommentHandler,
    DeleteBlogCommentHandler deleteCommentHandler,
    GetBlogReactionsHandler getReactionsHandler,
    GetBlogCommentsHandler getCommentsHandler) : ControllerBase
{
    private CurrentUser AppUser => currentUser.RequiredUser;

    // ───── Reactions ─────

    [HttpGet("reactions")]
    [AllowAnonymous]
    [ProducesResponseType<GetBlogReactionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetBlogReactionsResponse>> GetReactions(string slug, CancellationToken ct)
    {
        if (BlogPostSlug.TryCreate(slug) is not { } postSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var reactions = await getReactionsHandler.HandleAsync(new GetBlogReactionsQuery(postSlug), ct);
        return GetBlogReactionsResponse.Serialize(reactions, currentUser.User?.Id);
    }

    [HttpPost("reactions/toggle")]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType<GetBlogReactionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetBlogReactionsResponse>> ToggleReaction(
        string slug,
        [FromBody] ToggleBlogReactionRequest request,
        CancellationToken ct)
    {
        if (BlogPostSlug.TryCreate(slug) is not { } postSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        // JsonStringEnumConverter rejects unknown names but still lets raw numbers through.
        if (!Enum.IsDefined(request.Kind))
            return this.ValidationError("kind", ToggleReactionError.UnknownKind);

        var command = new ToggleBlogReactionCommand(
            Slug: postSlug,
            User: AppUser,
            Kind: request.Kind,
            Timestamp: timeProvider.GetUtcNow());

        var reactions = await toggleReactionHandler.HandleAsync(command, ct);
        return GetBlogReactionsResponse.Serialize(reactions, AppUser.Id);
    }

    // ───── Comments ─────

    [HttpGet("comments")]
    [AllowAnonymous]
    [ProducesResponseType<ListBlogCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListBlogCommentsResponse>> GetComments(string slug, CancellationToken ct)
    {
        if (BlogPostSlug.TryCreate(slug) is not { } postSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var comments = await getCommentsHandler.HandleAsync(new GetBlogCommentsQuery(postSlug), ct);
        return ListBlogCommentsResponse.Serialize(comments);
    }

    [HttpPost("comments")]
    [EnableRateLimiting(RateLimitPolicies.BlogWrite)]
    [ProducesResponseType<BlogCommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BlogCommentResponse>> PostComment(
        string slug,
        [FromBody] PostBlogCommentRequest request,
        CancellationToken ct)
    {
        if (BlogPostSlug.TryCreate(slug) is not { } postSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var command = new PostBlogCommentCommand(
            Slug: postSlug,
            User: AppUser,
            Content: request.Content,
            ParentCommentId: request.ParentCommentId,
            Timestamp: timeProvider.GetUtcNow());

        var result = await postCommentHandler.HandleAsync(command, ct);

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
        if (BlogPostSlug.TryCreate(slug) is not { } postSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var command = new DeleteBlogCommentCommand(
            Slug: postSlug,
            CommentId: commentId,
            User: AppUser,
            Timestamp: timeProvider.GetUtcNow());

        var result = await deleteCommentHandler.HandleAsync(command, ct);

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
