using FluentValidation;
using Kalandra.Api.Features.JobOffers.Cancel;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Edit;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Features.JobOffers.UpdateStatus;
using Kalandra.Api.Infrastructure.Auth;
using Marten.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
public class JobOffersController(
    ICurrentUserAccessor currentUser,
    IValidator<CreateJobOfferRequest> createValidator,
    CreateJobOfferHandler createHandler,
    EditJobOfferHandler editHandler,
    ListJobOffersHandler listHandler,
    GetJobOfferDetailHandler detailHandler,
    JobOfferHistoryHandler historyHandler,
    CancelJobOfferHandler cancelHandler,
    UpdateJobOfferStatusHandler updateStatusHandler,
    CommentsHandler commentsHandler) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));
        }

        var (success, error, result) = await createHandler.HandleAsync(
            request,
            currentUser.RequireUserId(),
            currentUser.GetEmail() ?? "",
            ct);

        if (!success || result == null)
        {
            return BadRequest(new { error });
        }

        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Edit(
        Guid id,
        [FromBody] CreateJobOfferRequest request,
        CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));
        }

        try
        {
            var (success, error) = await editHandler.HandleAsync(
                id,
                request,
                currentUser.RequireUserId(),
                currentUser.GetEmail() ?? "",
                ct);

            if (!success)
            {
                return error == "Not found" ? NotFound()
                    : error == "Not authorized" ? Forbid()
                    : BadRequest(new { error });
            }

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
        var result = await listHandler.HandleAsync(currentUser.RequireUserId(), ct);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var result = await listHandler.HandleAsync(null, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await detailHandler.HandleAsync(
            id,
            currentUser.RequireUserId(),
            await currentUser.IsAdminAsync(),
            ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [Authorize]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var result = await historyHandler.HandleAsync(
            id,
            currentUser.RequireUserId(),
            await currentUser.IsAdminAsync(),
            ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await cancelHandler.HandleAsync(
                id,
                request,
                currentUser.RequireUserId(),
                currentUser.GetEmail() ?? "",
                ct);

            if (!success)
            {
                return error == "Not found" ? NotFound()
                    : error == "Not authorized" ? Forbid()
                    : BadRequest(new { error });
            }

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
        try
        {
            var (success, error) = await updateStatusHandler.HandleAsync(
                id,
                request,
                currentUser.RequireUserId(),
                currentUser.GetEmail() ?? "",
                ct);

            if (!success)
            {
                return error == "Not found"
                    ? NotFound()
                    : BadRequest(new { error });
            }

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
        var result = await commentsHandler.ListCommentsAsync(
            id,
            currentUser.RequireUserId(),
            await currentUser.IsAdminAsync(),
            ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize]
    public async Task<IActionResult> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await commentsHandler.AddCommentAsync(
                id,
                request,
                currentUser.RequireUserId(),
                currentUser.GetEmail() ?? "",
                currentUser.GetDisplayName(),
                await currentUser.IsAdminAsync(),
                ct);

            if (!success)
            {
                return error == "Not found" ? NotFound()
                    : error == "Not authorized" ? Forbid()
                    : BadRequest(new { error });
            }

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
