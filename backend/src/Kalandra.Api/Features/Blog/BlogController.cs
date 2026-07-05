using Kalandra.Api.Features.Blog.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Queries;
using Kalandra.Blog.Workflows;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Kalandra.Api.Features.Blog;

[ApiController]
[Route("api/blog/{slug}")]
[Produces("application/json")]
[Authorize]
public class BlogController(
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ITemporalClient temporalClient,
    ToggleBlogReactionHandler toggleReactionHandler,
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

        if (request.Content?.Trim().AsNonEmpty() is not { } content)
            return this.ValidationError("content", PostCommentError.ContentRequired);

        var comment = new BlogCommentPosted(
            CommentId: Guid.NewGuid(),
            ParentCommentId: request.ParentCommentId,
            UserId: AppUser.Id,
            UserEmail: AppUser.Email,
            AuthorDisplayName: AppUser.FullName,
            AuthorAvatarUrl: AppUser.AvatarUrl,
            Content: content,
            Timestamp: timeProvider.GetUtcNow());

        // Store + notify share one durable workflow (BlogCommentWorkflow); the
        // update returns once the comment is stored, notifications continue async.
        var input = new BlogCommentWorkflowInput(postSlug.Value, comment);
        var startOperation = WithStartWorkflowOperation.Create(
            (BlogCommentWorkflow workflow) => workflow.RunAsync(input),
            new(id: BlogCommentWorkflow.IdFor(comment.CommentId), taskQueue: BlogTaskQueue.Name)
            {
                // A client retry of the same request reattaches instead of failing.
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });

        // Cancellation frees this request if the client disconnects; the workflow
        // itself keeps running — durability is the point.
        var outcome = await temporalClient.ExecuteUpdateWithStartWorkflowAsync(
            (BlogCommentWorkflow workflow) => workflow.StoreCommentAsync(input),
            new(startOperation) { Rpc = new() { CancellationToken = ct } });

        if (outcome.Error is { } error)
        {
            return error switch
            {
                PostBlogCommentError.ParentCommentNotFound => this.ValidationError("parentCommentId", PostCommentError.ParentCommentNotFound),
                PostBlogCommentError.ParentCommentDeleted => this.ValidationError("parentCommentId", PostCommentError.ParentCommentDeleted),
            };
        }

        return BlogCommentResponse.Serialize(outcome.Posted!);
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
