using JasperFx;
using JasperFx.Events;
using Kalandra.Api.Features.JobOffers.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Queries;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
[Produces("application/json")]
[Authorize]
public class JobOffersController(
    ICurrentUserAccessor currentUser,
    IStorageService storageService,
    TimeProvider timeProvider,
    CreateJobOfferHandler createHandler,
    EditJobOfferHandler editHandler,
    CancelJobOfferHandler cancelHandler,
    UpdateJobOfferStatusHandler updateStatusHandler,
    AddCommentHandler addCommentHandler,
    GetJobOfferDetailHandler getDetailHandler,
    ListJobOffersHandler listHandler,
    GetJobOfferHistoryHandler historyHandler,
    ListCommentsHandler listCommentsHandler,
    GetAttachmentInfoHandler attachmentHandler,
    ITurnstileValidator turnstileValidator) : ControllerBase
{
    private CurrentUser AppUser => currentUser.RequiredUser;

    // ───── Create ─────

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.HireMeCreateUser)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetJobOfferDetailResponse>> Create(
        [FromForm] CreateJobOfferRequest request,
        [FromForm] List<IFormFile>? attachments,
        [FromForm(Name = "cf-turnstile-response")] string? turnstileToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(turnstileToken))
            return BadRequest(new { error = "CAPTCHA verification is required." });

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var turnstileValid = await turnstileValidator.ValidateAsync(turnstileToken, remoteIp, ct);
        if (!turnstileValid)
            return BadRequest(new { error = "CAPTCHA verification failed. Please try again." });

        var files = (attachments ?? [])
            .Select(f => new CreateJobOfferFile(
                FileName: f.FileName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                FileSize: f.Length,
                ContentType: f.ContentType.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                Content: f.OpenReadStream()))
            .ToList();

        try
        {
            var command = new CreateJobOfferCommand(
                User: AppUser,
                CompanyName: request.CompanyName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                ContactName: request.ContactName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                ContactEmail: request.ContactEmail.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                JobTitle: request.JobTitle.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                Description: request.Description.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                SalaryRange: request.SalaryRange,
                Location: request.Location,
                IsRemote: request.IsRemote,
                AdditionalNotes: request.AdditionalNotes,
                Files: files,
                Timestamp: timeProvider.GetUtcNow());

            var result = await createHandler.HandleAsync(command, ct);

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    CreateJobOfferError.TooManyAttachments =>
                        BadRequest(new { error = "Maximum 5 attachments allowed." }),
                    CreateJobOfferError.TotalSizeTooLarge =>
                        BadRequest(new { error = "Total attachment size must not exceed 15 MB." }),
                    CreateJobOfferError.DisallowedContentType =>
                        BadRequest(new { error = "One or more file types are not allowed." }),
                };
            }

            var streamId = result.Success.Get((Unit _) => new InvalidOperationException());

            var detail = await LoadDetailResponseAsync(streamId, ct);
            return CreatedAtAction(nameof(GetDetail), new { id = streamId }, detail);
        }
        finally
        {
            foreach (var file in files)
                file.Content.Dispose();
        }
    }

    // ───── Edit ─────

    [HttpPut("{id:guid}")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GetJobOfferDetailResponse>> Edit(
        Guid id,
        [FromBody] EditJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling<GetJobOfferDetailResponse>(async () =>
        {
            var command = new EditJobOfferCommand(
                Id: id,
                User: AppUser,
                CompanyName: request.CompanyName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                ContactName: request.ContactName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                ContactEmail: request.ContactEmail.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                JobTitle: request.JobTitle.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                Description: request.Description.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
                SalaryRange: request.SalaryRange,
                Location: request.Location,
                IsRemote: request.IsRemote,
                AdditionalNotes: request.AdditionalNotes,
                Timestamp: timeProvider.GetUtcNow());

            var result = await editHandler.HandleAsync(command, ct);

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    EditJobOfferError.NotFound => NotFound(),
                    EditJobOfferError.NotAuthorized => Forbid(),
                    EditJobOfferError.NotSubmittedStatus =>
                        BadRequest(new { error = "Can only edit offers with status Submitted." }),
                };
            }

            var offer = result.Success.Get((Unit _) => new InvalidOperationException());
            return GetJobOfferDetailResponse.Serialize(offer, AppUser);
        });
    }

    // ───── Cancel ─────

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GetJobOfferDetailResponse>> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling<GetJobOfferDetailResponse>(async () =>
        {
            var command = new CancelJobOfferCommand(
                Id: id,
                User: AppUser,
                Reason: request.Reason,
                Timestamp: timeProvider.GetUtcNow());

            var result = await cancelHandler.HandleAsync(command, ct);

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    CancelJobOfferError.NotFound => NotFound(),
                    CancelJobOfferError.NotAuthorized => Forbid(),
                    CancelJobOfferError.InvalidStatus =>
                        BadRequest(new { error = "Cannot cancel an offer that has already been accepted, declined, or cancelled." }),
                };
            }

            var offer = result.Success.Get((Unit _) => new InvalidOperationException());
            return GetJobOfferDetailResponse.Serialize(offer, AppUser);
        });
    }

    // ───── Update Status (Admin) ─────

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GetJobOfferDetailResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling<GetJobOfferDetailResponse>(async () =>
        {
            var command = new UpdateJobOfferStatusCommand(
                Id: id,
                NewStatus: request.Status,
                User: AppUser,
                Notes: request.AdminNotes,
                Timestamp: timeProvider.GetUtcNow());

            var result = await updateStatusHandler.HandleAsync(command, ct);

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    UpdateJobOfferStatusError.NotFound => NotFound(),
                    UpdateJobOfferStatusError.AlreadyInStatus =>
                        BadRequest(new { error = "Job offer is already in the requested status." }),
                    UpdateJobOfferStatusError.InvalidTransition =>
                        BadRequest(new { error = "The requested status transition is not allowed." }),
                };
            }

            var offer = result.Success.Get((Unit _) => new InvalidOperationException());
            return GetJobOfferDetailResponse.Serialize(offer, AppUser);
        });
    }

    // ───── Add Comment ─────

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType<CommentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentResponse>> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Comment content is required." });

        var command = new AddCommentCommand(
            JobOfferId: id,
            User: AppUser,
            Content: request.Content.Trim().AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
            Timestamp: timeProvider.GetUtcNow());

        var result = await addCommentHandler.HandleAsync(command, ct);

        if (result.IsError)
        {
            var error = result.Error.Get((Unit _) => new InvalidOperationException());
            return error switch
            {
                AddCommentError.NotFound => NotFound(),
                AddCommentError.NotAuthorized => Forbid(),
            };
        }

        var commentEvent = result.Success.Get((Unit _) => new InvalidOperationException());
        return CommentResponse.Serialize(commentEvent);
    }

    // ───── List ─────

    [HttpGet("mine")]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ListJobOffersResponse>> ListMine(
        [FromQuery] JobOfferStatus[]? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await ListOffersAsync(showAll: false, status, page, pageSize, ct);
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListJobOffersResponse>> ListAll(
        [FromQuery] JobOfferStatus[]? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await ListOffersAsync(showAll: true, status, page, pageSize, ct);
    }

    // ───── Get Detail ─────

    [HttpGet("{id:guid}")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetJobOfferDetailResponse>> GetDetail(Guid id, CancellationToken ct)
    {
        var detail = await LoadDetailResponseAsync(id, ct);
        if (detail == null)
            return NotFound();
        return detail;
    }

    // ───── Download Attachment ─────

    [HttpGet("{id:guid}/attachments/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid id, string fileName, CancellationToken ct)
    {
        var query = new GetAttachmentInfoQuery(
            JobOfferId: id,
            FileName: fileName,
            User: AppUser);

        var info = await attachmentHandler.HandleAsync(query, ct);
        if (info == null)
            return NotFound();

        var download = await storageService.DownloadAsync(info.StoragePath, ct);
        if (download == null)
            return NotFound();

        return File(
            fileStream: download.Content,
            contentType: download.ContentType,
            fileDownloadName: info.Attachment.FileName,
            enableRangeProcessing: true);
    }

    // ───── History ─────

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<JobOfferHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobOfferHistoryResponse>> GetHistory(Guid id, CancellationToken ct)
    {
        var query = new GetJobOfferHistoryQuery(Id: id, User: AppUser);
        var entries = await historyHandler.HandleAsync(query, ct);
        if (entries == null)
            return NotFound();

        return new JobOfferHistoryResponse(entries.ToList());
    }

    // ───── List Comments ─────

    [HttpGet("{id:guid}/comments")]
    [ProducesResponseType<ListCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListCommentsResponse>> ListComments(Guid id, CancellationToken ct)
    {
        var query = new ListCommentsQuery(JobOfferId: id, User: AppUser);
        var comments = await listCommentsHandler.HandleAsync(query, ct);
        if (comments == null)
            return NotFound();

        return new ListCommentsResponse(comments.Select(CommentResponse.Serialize).ToList());
    }

    // ───── Private helpers ─────

    private async Task<GetJobOfferDetailResponse?> LoadDetailResponseAsync(Guid id, CancellationToken ct)
    {
        var query = new GetJobOfferDetailQuery(Id: id, User: AppUser);
        var offer = await getDetailHandler.HandleAsync(query, ct);
        return offer == null ? null : GetJobOfferDetailResponse.Serialize(offer, AppUser);
    }

    private async Task<ListJobOffersResponse> ListOffersAsync(
        bool showAll,
        JobOfferStatus[]? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = new ListJobOffersQuery(
            User: AppUser,
            ShowAll: showAll,
            Statuses: status,
            Page: page,
            PageSize: pageSize);

        var result = await listHandler.HandleAsync(query, ct);

        return new ListJobOffersResponse(
            result.Items.Select(j => GetJobOfferDetailResponse.Serialize(j, AppUser)).ToList(),
            result.TotalCount);
    }

    private async Task<ActionResult<T>> WithConcurrencyHandling<T>(Func<Task<ActionResult<T>>> action)
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
