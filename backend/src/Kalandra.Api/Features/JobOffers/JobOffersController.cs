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
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetJobOfferDetailResponse>> Create(
        [FromForm] CreateJobOfferRequest request,
        [FromForm] List<IFormFile>? attachments,
        [FromForm(Name = "cf-turnstile-response")] string? turnstileToken,
        CancellationToken ct)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!await turnstileValidator.ValidateAsync(turnstileToken, remoteIp, ct))
            return this.ValidationError("captcha", CreateOfferError.CaptchaFailed);

        // OpenReadStream() wraps ASP.NET Core's internal buffer — disposed by the framework at end of request
        var files = (attachments ?? [])
            .Select(f => new CreateJobOfferFile(
                FileName: f.FileName.AsNonEmpty().Get(),
                FileSize: f.Length,
                ContentType: f.ContentType.AsNonEmpty().Get(),
                Content: f.OpenReadStream()))
            .ToList();

        var command = new CreateJobOfferCommand(
            User: AppUser,
            CompanyName: request.CompanyName.AsNonEmpty().Get(),
            ContactName: request.ContactName.AsNonEmpty().Get(),
            ContactEmail: request.ContactEmail.AsNonEmpty().Get(),
            JobTitle: request.JobTitle.AsNonEmpty().Get(),
            Description: request.Description.AsNonEmpty().Get(),
            SalaryRange: request.SalaryRange,
            Location: request.Location,
            IsRemote: request.IsRemote,
            AdditionalNotes: request.AdditionalNotes,
            Files: files,
            Timestamp: timeProvider.GetUtcNow());

        var result = await createHandler.HandleAsync(command, ct);

        if (result.IsError)
        {
            return result.Error.Get() switch
            {
                CreateJobOfferError.TooManyAttachments => this.ValidationError("attachments", CreateOfferError.TooManyAttachments),
                CreateJobOfferError.TotalSizeTooLarge => this.ValidationError("attachments", CreateOfferError.TotalSizeTooLarge),
                CreateJobOfferError.DisallowedContentType => this.ValidationError("attachments", CreateOfferError.DisallowedContentType),
            };
        }

        var streamId = result.Success.Get();

        var query = new GetJobOfferDetailQuery(Id: streamId, User: AppUser);
        var offer = await getDetailHandler.HandleAsync(query, ct);
        return CreatedAtAction(nameof(GetDetail), new { id = streamId },
            GetJobOfferDetailResponse.Serialize(offer!));
    }

    // ───── Edit ─────

    [HttpPut("{id:guid}")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
                CompanyName: request.CompanyName.AsNonEmpty().Get(),
                ContactName: request.ContactName.AsNonEmpty().Get(),
                ContactEmail: request.ContactEmail.AsNonEmpty().Get(),
                JobTitle: request.JobTitle.AsNonEmpty().Get(),
                Description: request.Description.AsNonEmpty().Get(),
                SalaryRange: request.SalaryRange,
                Location: request.Location,
                IsRemote: request.IsRemote,
                AdditionalNotes: request.AdditionalNotes,
                Timestamp: timeProvider.GetUtcNow());

            var result = await editHandler.HandleAsync(command, ct);

            if (result.IsError)
            {
                return result.Error.Get() switch
                {
                    EditJobOfferError.NotFound => NotFound(),
                    EditJobOfferError.NotAuthorized => Forbid(),
                    EditJobOfferError.NotSubmittedStatus => this.ValidationError("status", EditOfferError.NotSubmittedStatus),
                };
            }

            var offer = result.Success.Get();
            return GetJobOfferDetailResponse.Serialize(offer);
        });
    }

    // ───── Cancel ─────

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
                return result.Error.Get() switch
                {
                    CancelJobOfferError.NotFound => NotFound(),
                    CancelJobOfferError.NotAuthorized => Forbid(),
                    CancelJobOfferError.InvalidStatus => this.ValidationError("status", CancelOfferError.InvalidStatus),
                };
            }

            var offer = result.Success.Get();
            return GetJobOfferDetailResponse.Serialize(offer);
        });
    }

    // ───── Update Status (Admin) ─────

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
                return result.Error.Get() switch
                {
                    UpdateJobOfferStatusError.NotFound => NotFound(),
                    UpdateJobOfferStatusError.InvalidTransition =>
                        this.ValidationError("status", UpdateOfferStatusError.InvalidTransition),
                };
            }

            var offer = result.Success.Get();
            return GetJobOfferDetailResponse.Serialize(offer);
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
        var command = new AddCommentCommand(
            JobOfferId: id,
            User: AppUser,
            Content: request.Content.Trim().AsNonEmpty().Get(),
            Timestamp: timeProvider.GetUtcNow());

        var result = await addCommentHandler.HandleAsync(command, ct);

        if (result.IsError)
        {
            return result.Error.Get() switch
            {
                AddCommentError.NotFound => NotFound(),
                AddCommentError.NotAuthorized => Forbid(),
            };
        }

        var commentEvent = result.Success.Get();
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
        var query = new GetJobOfferDetailQuery(Id: id, User: AppUser);
        var offer = await getDetailHandler.HandleAsync(query, ct);
        if (offer == null)
            return NotFound();
        return GetJobOfferDetailResponse.Serialize(offer);
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

        return new JobOfferHistoryResponse(entries.Select(HistoryEntryResponse.Serialize));
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

        return new ListCommentsResponse(comments.Select(CommentResponse.Serialize));
    }

    // ───── Private helpers ─────

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
            Items: result.Select(GetJobOfferDetailResponse.Serialize).ToList(),
            Pagination: PaginationMetadata.FromPagedList(result));
    }

    private async Task<ActionResult<T>> WithConcurrencyHandling<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
        {
            return Conflict(ProblemDetailsFactory.CreateProblemDetails(
                HttpContext,
                statusCode: StatusCodes.Status409Conflict,
                detail: "Job offer was modified by another request."));
        }
    }

}
