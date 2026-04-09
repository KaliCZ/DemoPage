using JasperFx;
using JasperFx.Events;
using Kalandra.Api.Features.JobOffers.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
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
    IDocumentSession session,
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
    public async Task<IActionResult> Create(
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
                UserId: NonEmptyString.CreateUnsafe(AppUser.Id.ToString()),
                UserEmail: AppUser.Email.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
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
            await session.SaveChangesAsync(ct);

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
    public Task<IActionResult> Edit(
        Guid id,
        [FromBody] EditJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling(async () =>
        {
            var command = new EditJobOfferCommand(
                Id: id,
                UserId: NonEmptyString.CreateUnsafe(AppUser.Id.ToString()),
                UserEmail: AppUser.Email.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
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

            await session.SaveChangesAsync(ct);
            return Ok(await LoadDetailResponseAsync(id, ct));
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
    public Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        return WithConcurrencyHandling(async () =>
        {
            var command = new CancelJobOfferCommand(
                Id: id,
                UserId: NonEmptyString.CreateUnsafe(AppUser.Id.ToString()),
                UserEmail: AppUser.Email.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
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

            await session.SaveChangesAsync(ct);
            return Ok(await LoadDetailResponseAsync(id, ct));
        });
    }

    // ───── Update Status (Admin) ─────

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
            var command = new UpdateJobOfferStatusCommand(
                Id: id,
                NewStatus: request.Status,
                ChangedByUserId: NonEmptyString.CreateUnsafe(AppUser.Id.ToString()),
                ChangedByEmail: AppUser.Email.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
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

            await session.SaveChangesAsync(ct);
            return Ok(await LoadDetailResponseAsync(id, ct));
        });
    }

    // ───── Add Comment ─────

    [HttpPost("{id:guid}/comments")]
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
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Comment content is required." });

        var command = new AddCommentCommand(
            JobOfferId: id,
            UserId: NonEmptyString.CreateUnsafe(AppUser.Id.ToString()),
            UserEmail: AppUser.Email.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
            UserName: AppUser.DisplayName.AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
            Content: request.Content.Trim().AsNonEmpty().Get((Unit _) => new InvalidOperationException()),
            IsAdmin: AppUser.IsAdmin,
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

        await session.SaveChangesAsync(ct);

        var commentEvent = result.Success.Get((Unit _) => new InvalidOperationException());
        return Ok(new CommentResponse(
            Id: commentEvent.CommentId,
            UserId: commentEvent.UserId,
            UserEmail: commentEvent.UserEmail,
            UserName: commentEvent.UserName,
            Content: commentEvent.Content,
            CreatedAt: commentEvent.Timestamp));
    }

    // ───── List ─────

    [HttpGet("mine")]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMine(
        [FromQuery] JobOfferStatus[]? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await ListOffersAsync(showAll: false, status, page, pageSize, ct));
    }

    [HttpGet]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAll(
        [FromQuery] JobOfferStatus[]? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await ListOffersAsync(showAll: true, status, page, pageSize, ct));
    }

    // ───── Get Detail ─────

    [HttpGet("{id:guid}")]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var detail = await LoadDetailResponseAsync(id, ct);
        return detail == null ? NotFound() : Ok(detail);
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
            UserId: AppUser.Id.ToString(),
            IsAdmin: AppUser.IsAdmin);

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
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var query = new GetJobOfferHistoryQuery(Id: id, UserId: AppUser.Id.ToString(), IsAdmin: AppUser.IsAdmin);
        var entries = await historyHandler.HandleAsync(query, ct);
        if (entries == null)
            return NotFound();

        return Ok(new JobOfferHistoryResponse(entries.ToList()));
    }

    // ───── List Comments ─────

    [HttpGet("{id:guid}/comments")]
    [ProducesResponseType<ListCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListComments(Guid id, CancellationToken ct)
    {
        var query = new ListCommentsQuery(JobOfferId: id, UserId: AppUser.Id.ToString(), IsAdmin: AppUser.IsAdmin);
        var comments = await listCommentsHandler.HandleAsync(query, ct);
        if (comments == null)
            return NotFound();

        return Ok(new ListCommentsResponse(comments.Select(c => new CommentResponse(
            Id: c.CommentId,
            UserId: c.UserId,
            UserEmail: c.UserEmail,
            UserName: c.UserName,
            Content: c.Content,
            CreatedAt: c.Timestamp)).ToList()));
    }

    // ───── Private helpers ─────

    private async Task<GetJobOfferDetailResponse?> LoadDetailResponseAsync(Guid id, CancellationToken ct)
    {
        var query = new GetJobOfferDetailQuery(Id: id, UserId: AppUser.Id.ToString(), IsAdmin: AppUser.IsAdmin);
        var offer = await getDetailHandler.HandleAsync(query, ct);
        if (offer == null)
            return null;

        return new GetJobOfferDetailResponse(
            Id: offer.Id,
            CompanyName: offer.CompanyName,
            ContactName: offer.ContactName,
            ContactEmail: offer.ContactEmail,
            JobTitle: offer.JobTitle,
            Description: offer.Description,
            SalaryRange: offer.SalaryRange,
            Location: offer.Location,
            IsRemote: offer.IsRemote,
            AdditionalNotes: offer.AdditionalNotes,
            Attachments: offer.Attachments,
            Status: offer.Status,
            AdminNotes: AppUser.IsAdmin ? offer.AdminNotes : null,
            UserEmail: offer.UserEmail,
            CreatedAt: offer.CreatedAt,
            UpdatedAt: offer.UpdatedAt);
    }

    private async Task<ListJobOffersResponse> ListOffersAsync(
        bool showAll,
        JobOfferStatus[]? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = new ListJobOffersQuery(
            UserId: AppUser.Id.ToString(),
            IsAdmin: showAll,
            Statuses: status,
            Page: page,
            PageSize: pageSize);

        var result = await listHandler.HandleAsync(query, ct);

        return new ListJobOffersResponse(
            result.Items.Select(j => new JobOfferSummary(
                Id: j.Id,
                CompanyName: j.CompanyName,
                JobTitle: j.JobTitle,
                ContactEmail: j.ContactEmail,
                Status: j.Status,
                IsRemote: j.IsRemote,
                Location: j.Location,
                CreatedAt: j.CreatedAt)).ToList(),
            result.TotalCount);
    }

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
