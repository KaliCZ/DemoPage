using FluentValidation;
using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Cancel;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Edit;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Features.JobOffers.UpdateStatus;
using Kalandra.Api.Infrastructure.Auth;
using Marten;
using Marten.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
public class JobOffersController(IDocumentSession session) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        [FromServices] IValidator<CreateJobOfferRequest> validator,
        [FromServices] IJobOfferAttachmentVerifier attachmentVerifier,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";

        var handler = new CreateJobOfferHandler(session, attachmentVerifier);
        var (success, error, result) = await handler.HandleAsync(request, userId, email, ct);

        if (!success || result == null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Edit(
        Guid id,
        [FromBody] CreateJobOfferRequest request,
        [FromServices] IValidator<CreateJobOfferRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";

        var handler = new EditJobOfferHandler(session);
        try
        {
            var (success, error) = await handler.HandleAsync(id, request, userId, email, ct);

            if (!success)
                return error == "Not found" ? NotFound() :
                       error == "Not authorized" ? Forbid() :
                       BadRequest(new { error });

            return NoContent();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Conflict(new { error = "Job offer was modified by another request. Please refresh and try again." });
        }
    }

    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var handler = new ListJobOffersHandler(session);
        var result = await handler.HandleAsync(userId, ct);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var handler = new ListJobOffersHandler(session);
        var result = await handler.HandleAsync(null, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new GetJobOfferDetailHandler(session);
        var result = await handler.HandleAsync(id, userId, isAdmin, ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [Authorize]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new JobOfferHistoryHandler(session);
        var result = await handler.HandleAsync(id, userId, isAdmin, ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";

        var handler = new CancelJobOfferHandler(session);
        try
        {
            var (success, error) = await handler.HandleAsync(id, request, userId, email, ct);

            if (!success)
                return error == "Not found" ? NotFound() :
                       error == "Not authorized" ? Forbid() :
                       BadRequest(new { error });

            return NoContent();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Conflict(new { error = "Job offer was modified by another request. Please refresh and try again." });
        }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        var adminUserId = User.GetUserId()!;
        var adminEmail = User.GetEmail() ?? "";

        var handler = new UpdateJobOfferStatusHandler(session);
        try
        {
            var (success, error) = await handler.HandleAsync(id, request, adminUserId, adminEmail, ct);

            if (!success)
                return error == "Not found"
                    ? NotFound()
                    : BadRequest(new { error });

            return NoContent();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Conflict(new { error = "Job offer was modified by another request. Please refresh and try again." });
        }
    }

    [HttpGet("{id:guid}/comments")]
    [Authorize]
    public async Task<IActionResult> ListComments(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new CommentsHandler(session);
        var result = await handler.ListCommentsAsync(id, userId, isAdmin, ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize]
    public async Task<IActionResult> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";
        var name = User.FindFirst("user_metadata.full_name")?.Value
            ?? User.FindFirst("name")?.Value
            ?? email.Split('@')[0];
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new CommentsHandler(session);
        try
        {
            var (success, error) = await handler.AddCommentAsync(id, request, userId, email, name, isAdmin, ct);

            if (!success)
                return error == "Not found" ? NotFound() :
                       error == "Not authorized" ? Forbid() :
                       BadRequest(new { error });

            return NoContent();
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Conflict(new { error = "Job offer was modified by another request. Please refresh and try again." });
        }
    }

    private static bool IsConcurrencyConflict(Exception exception) =>
        exception is ConcurrencyException or EventStreamUnexpectedMaxEventIdException;
}
