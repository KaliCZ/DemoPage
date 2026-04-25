using System.Text.RegularExpressions;
using Kalandra.Api.Features.Blog.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Queries;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Blog;

[ApiController]
[Route("api/blog")]
[Produces("application/json")]
public partial class BlogController(
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    AddBlogCommentHandler addCommentHandler,
    ToggleBlogReactionHandler toggleReactionHandler,
    ListBlogCommentsHandler listCommentsHandler,
    GetBlogReactionsHandler getReactionsHandler) : ControllerBase
{
    // Slug grammar: lowercase letters, digits, dashes — bounded so a typo
    // can't accidentally create a multi-megabyte stream ID. Same shape used
    // on the frontend, where slugs come from blog post .astro file names.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    private CurrentUser AppUser => currentUser.RequiredUser;

    // ───── Add Comment ─────

    [HttpPost("{slug}/comments")]
    [Authorize]
    [ProducesResponseType<BlogCommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BlogCommentResponse>> AddComment(
        string slug,
        [FromBody] AddBlogCommentRequest request,
        CancellationToken ct)
    {
        if (TryParseSlug(slug) is not { } parsedSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        if (request.Content.Trim().AsNonEmpty().GetOrNull() is not { } content)
            return this.ValidationError("content", AddBlogCommentError.ContentRequired);

        var command = new AddBlogCommentCommand(
            Slug: parsedSlug,
            User: AppUser,
            Content: content,
            Timestamp: timeProvider.GetUtcNow());

        var commentEvent = await addCommentHandler.HandleAsync(command, ct);
        return BlogCommentResponse.Serialize(commentEvent);
    }

    // ───── Toggle Reaction ─────

    [HttpPost("{slug}/reactions")]
    [Authorize]
    [ProducesResponseType<ToggleBlogReactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ToggleBlogReactionResponse>> ToggleReaction(
        string slug,
        [FromBody] BlogReactionRequest request,
        CancellationToken ct)
    {
        if (TryParseSlug(slug) is not { } parsedSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var command = new ToggleBlogReactionCommand(
            Slug: parsedSlug,
            User: AppUser,
            Emoji: request.Emoji,
            Timestamp: timeProvider.GetUtcNow());

        var result = await toggleReactionHandler.HandleAsync(command, ct);
        return new ToggleBlogReactionResponse(result.Emoji, result.Action);
    }

    // ───── List Comments ─────

    [HttpGet("{slug}/comments")]
    [AllowAnonymous]
    [ProducesResponseType<ListBlogCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListBlogCommentsResponse>> ListComments(string slug, CancellationToken ct)
    {
        if (TryParseSlug(slug) is not { } parsedSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        var comments = await listCommentsHandler.HandleAsync(new ListBlogCommentsQuery(parsedSlug), ct);
        return new ListBlogCommentsResponse(comments.Select(BlogCommentResponse.Serialize));
    }

    // ───── Get Reactions ─────

    [HttpGet("{slug}/reactions")]
    [AllowAnonymous]
    [ProducesResponseType<BlogReactionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BlogReactionsResponse>> GetReactions(string slug, CancellationToken ct)
    {
        if (TryParseSlug(slug) is not { } parsedSlug)
            return this.ValidationError("slug", BlogSlugError.InvalidSlug);

        // Anonymous viewers see counts but no user-specific reactions. The
        // accessor returns null when no JWT is present.
        var viewerId = currentUser.User?.Id;
        var view = await getReactionsHandler.HandleAsync(new GetBlogReactionsQuery(parsedSlug, viewerId), ct);
        return BlogReactionsResponse.Serialize(view);
    }

    private static NonEmptyString? TryParseSlug(string slug) =>
        SlugRegex().IsMatch(slug) ? slug.AsNonEmpty().GetOrNull() : null;
}
