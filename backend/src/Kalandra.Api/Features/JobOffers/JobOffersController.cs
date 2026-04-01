using System.Diagnostics;
using Kalandra.Api.Features.JobOffers.Attachments;
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
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        CancellationToken ct)
    {
        var result = await createHandler.HandleAsync(
            request: request,
            userId: AppUser.Id,
            userEmail: AppUser.Email,
            ct: ct);

        if (result.IsError)
        {
            return result.Match<IActionResult>(
                _ => throw new UnreachableException(),
                error => error switch
                {
                    CreateJobOfferError.AttachmentServiceUnavailable =>
                        BadRequest(new { error = "Attachments are temporarily unavailable." }),
                    CreateJobOfferError.AttachmentPathTraversal =>
                        BadRequest(new { error = "Attachment paths must stay within the user's offer folder." }),
                    CreateJobOfferError.AttachmentWrongFolder =>
                        BadRequest(new { error = "Attachments must be uploaded into the current offer folder." }),
                    CreateJobOfferError.AttachmentMetadataMismatch =>
                        BadRequest(new { error = "Attachment metadata does not match the uploaded file." }),
                    CreateJobOfferError.AttachmentFileNotFound =>
                        BadRequest(new { error = "One or more attachments were not found in storage." }),
                });
        }

        var created = result.Success.Get((Unit _) => new UnreachableException());
        var detail = await LoadDetailAsync(created.Id, ct);
        return CreatedAtAction(nameof(GetDetail), new { id = created.Id }, detail);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Edit(
        Guid id,
        [FromBody] EditJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling(async () =>
        {
            var result = await editHandler.HandleAsync(
                id: id,
                request: request,
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                ct: ct);

            if (result.IsError)
            {
                return result.Match<IActionResult>(
                    _ => throw new UnreachableException(),
                    error => error switch
                    {
                        EditJobOfferError.NotFound => NotFound(),
                        EditJobOfferError.NotAuthorized => Forbid(),
                        EditJobOfferError.NotSubmittedStatus =>
                            BadRequest(new { error = "Can only edit offers with status Submitted." }),
                    });
            }

            return Ok(await LoadDetailAsync(id, ct));
        });
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
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling(async () =>
        {
            var result = await cancelHandler.HandleAsync(
                id: id,
                request: request,
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                ct: ct);

            if (result.IsError)
            {
                return result.Match<IActionResult>(
                    _ => throw new UnreachableException(),
                    error => error switch
                    {
                        CancelJobOfferError.NotFound => NotFound(),
                        CancelJobOfferError.NotAuthorized => Forbid(),
                        CancelJobOfferError.InvalidStatus =>
                            BadRequest(new { error = "Cannot cancel an offer that has already been accepted, declined, or cancelled." }),
                    });
            }

            return Ok(await LoadDetailAsync(id, ct));
        });
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling(async () =>
        {
            var result = await updateStatusHandler.HandleAsync(
                id: id,
                request: request,
                adminUserId: AppUser.Id,
                adminEmail: AppUser.Email,
                ct: ct);

            if (result.IsError)
            {
                return result.Match<IActionResult>(
                    _ => throw new UnreachableException(),
                    error => error switch
                    {
                        UpdateJobOfferStatusError.NotFound => NotFound(),
                        UpdateJobOfferStatusError.AlreadyInStatus =>
                            BadRequest(new { error = "Job offer is already in the requested status." }),
                        UpdateJobOfferStatusError.InvalidTransition =>
                            BadRequest(new { error = "The requested status transition is not allowed." }),
                    });
            }

            return Ok(await LoadDetailAsync(id, ct));
        });
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
    [ProducesResponseType<CommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        var result = await addCommentHandler.HandleAsync(
            jobOfferId: id,
            request: request,
            userId: AppUser.Id,
            userEmail: AppUser.Email,
            userName: AppUser.DisplayName,
            isAdmin: AppUser.IsAdmin,
            ct: ct);

        return result.Match<IActionResult>(
            comment => Ok(comment),
            error => error switch
            {
                AddJobOfferCommentError.NotFound => NotFound(),
                AddJobOfferCommentError.NotAuthorized => Forbid(),
                AddJobOfferCommentError.ContentRequired =>
                    BadRequest(new { error = "Comment content is required." }),
            });
    }

    private async Task<GetJobOfferDetailResponse> LoadDetailAsync(Guid id, CancellationToken ct)
        => (await detailHandler.HandleAsync(
            id: id,
            requesterUserId: AppUser.Id,
            isAdmin: AppUser.IsAdmin,
            ct: ct))!;

    private async Task<IActionResult> WithConcurrencyHandling(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
        {
            return Conflict(new { error = "Job offer was modified by another request. Please refresh and try again." });
        }
    }
}
