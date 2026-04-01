using Kalandra.Api.Features.JobOffers.Cancel;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Edit;
using Kalandra.Api.Features.JobOffers.Entities;
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
[Produces("application/json")]
public class JobOffersController(
    ICurrentUserAccessor currentUser,
    CreateJobOfferHandler createHandler,
    EditJobOfferHandler editHandler,
    ListJobOffersHandler listHandler,
    GetJobOfferDetailHandler detailHandler,
    JobOfferHistoryHandler historyHandler,
    CancelJobOfferHandler cancelHandler,
    UpdateJobOfferStatusHandler updateStatusHandler,
    AddCommentHandler addCommentHandler,
    ListCommentsHandler listCommentsHandler) : ControllerBase
{
    private CurrentUser AppUser => currentUser.CurrentUser;

    [HttpPost]
    [Authorize]
    [ProducesResponseType<CreateJobOfferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        CancellationToken ct)
    {
        var (success, error, result) = await createHandler.HandleAsync(
            request: request,
            userId: AppUser.Id,
            userEmail: AppUser.Email,
            ct: ct);

        if (!success || result == null)
        {
            return BadRequest(new { error });
        }

        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Edit(
        Guid id,
        [FromBody] EditJobOfferRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await editHandler.HandleAsync(
                id: id,
                request: request,
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                ct: ct);

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
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMine(
        [FromQuery] JobOfferStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await listHandler.HandleAsync(
            status: status,
            page: page,
            pageSize: pageSize,
            ct: ct);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAll(
        [FromQuery] JobOfferStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await listHandler.HandleAsync(
            status: status,
            page: page,
            pageSize: pageSize,
            ct: ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await detailHandler.HandleAsync(
            id: id,
            requesterUserId: AppUser.Id,
            isAdmin: AppUser.IsAdmin,
            ct: ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProducesResponseType<JobOfferHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var result = await historyHandler.HandleAsync(
            id: id,
            requesterUserId: AppUser.Id,
            isAdmin: AppUser.IsAdmin,
            ct: ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await cancelHandler.HandleAsync(
                id: id,
                request: request,
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                ct: ct);

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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await updateStatusHandler.HandleAsync(
                id: id,
                request: request,
                adminUserId: AppUser.Id,
                adminEmail: AppUser.Email,
                ct: ct);

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
    [ProducesResponseType<ListCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListComments(Guid id, CancellationToken ct)
    {
        var result = await listCommentsHandler.HandleAsync(
            jobOfferId: id,
            requesterUserId: AppUser.Id,
            isAdmin: AppUser.IsAdmin,
            ct: ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        try
        {
            var (success, error) = await addCommentHandler.HandleAsync(
                jobOfferId: id,
                request: request,
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                userName: AppUser.DisplayName,
                isAdmin: AppUser.IsAdmin,
                ct: ct);

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
