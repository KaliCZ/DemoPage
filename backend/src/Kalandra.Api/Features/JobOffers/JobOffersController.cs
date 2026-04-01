using Kalandra.Api.Features.JobOffers.Contracts;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
[Produces("application/json")]
public class JobOffersController(
    ICurrentUserAccessor currentUser,
    IDocumentSession session,
    IStorageFileVerifier fileVerifier,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaxPageSize = 100;

    private CurrentUser AppUser => currentUser.CurrentUser;

    // ───── Create ─────

    [HttpPost]
    [Authorize]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        CancellationToken ct)
    {
        var streamId = request.Id ?? Guid.NewGuid();
        var expectedPrefix = $"{AppUser.Id}/{streamId}/";

        var storageFiles = request.Attachments?
            .Select(a => new StorageFileInfo(a.FileName, a.StoragePath, a.FileSize, a.ContentType))
            .ToList();

        var verificationResult = await fileVerifier.VerifyAsync(expectedPrefix, storageFiles, ct);
        if (verificationResult.IsError)
        {
            var error = verificationResult.Error.Get((Unit _) => new InvalidOperationException());
            return BadRequest(new
            {
                error = error switch
                {
                    FileVerificationError.PathTraversal => "Attachment paths must stay within the user's offer folder.",
                    FileVerificationError.WrongFolder => "Attachments must be uploaded into the current offer folder.",
                    FileVerificationError.MetadataMismatch => "Attachment metadata does not match the uploaded file.",
                    FileVerificationError.FileNotFound => "One or more attachments were not found in storage.",
                }
            });
        }

        var verifiedFiles = verificationResult.Success.Get((Unit _) => new InvalidOperationException());
        var verifiedAttachments = verifiedFiles
            .Select(f => new AttachmentInfo(f.FileName, f.StoragePath, f.FileSize, f.ContentType))
            .ToList();

        var submitted = new JobOfferSubmitted(
            UserId: AppUser.Id,
            UserEmail: AppUser.Email,
            CompanyName: request.CompanyName,
            ContactName: request.ContactName,
            ContactEmail: request.ContactEmail,
            JobTitle: request.JobTitle,
            Description: request.Description,
            SalaryRange: request.SalaryRange,
            Location: request.Location,
            IsRemote: request.IsRemote,
            AdditionalNotes: request.AdditionalNotes,
            Attachments: verifiedAttachments,
            Timestamp: timeProvider.GetUtcNow());

        session.Events.StartStream<JobOffer>(streamId, submitted);
        await session.SaveChangesAsync(ct);

        var detail = await LoadDetailAsync(streamId, ct);
        return CreatedAtAction(nameof(GetDetail), new { id = streamId }, detail);
    }

    // ───── Edit ─────

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
            var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
            var offer = stream.Aggregate;
            if (offer == null)
                return NotFound();

            var result = offer.Edit(
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                companyName: request.CompanyName,
                contactName: request.ContactName,
                contactEmail: request.ContactEmail,
                jobTitle: request.JobTitle,
                description: request.Description,
                salaryRange: request.SalaryRange,
                location: request.Location,
                isRemote: request.IsRemote,
                additionalNotes: request.AdditionalNotes,
                timestamp: timeProvider.GetUtcNow());

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    EditJobOfferError.NotAuthorized => Forbid(),
                    EditJobOfferError.NotSubmittedStatus =>
                        BadRequest(new { error = "Can only edit offers with status Submitted." }),
                };
            }

            stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
            await session.SaveChangesAsync(ct);

            return Ok(await LoadDetailAsync(id, ct));
        });
    }

    // ───── Cancel ─────

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
            var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
            var offer = stream.Aggregate;
            if (offer == null)
                return NotFound();

            var result = offer.Cancel(
                userId: AppUser.Id,
                userEmail: AppUser.Email,
                reason: request.Reason,
                timestamp: timeProvider.GetUtcNow());

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    CancelJobOfferError.NotAuthorized => Forbid(),
                    CancelJobOfferError.InvalidStatus =>
                        BadRequest(new { error = "Cannot cancel an offer that has already been accepted, declined, or cancelled." }),
                };
            }

            stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
            await session.SaveChangesAsync(ct);

            return Ok(await LoadDetailAsync(id, ct));
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
            var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
            var offer = stream.Aggregate;
            if (offer == null)
                return NotFound();

            var result = offer.ChangeStatus(
                newStatus: request.Status,
                changedByUserId: AppUser.Id,
                changedByEmail: AppUser.Email,
                notes: request.AdminNotes,
                timestamp: timeProvider.GetUtcNow());

            if (result.IsError)
            {
                var error = result.Error.Get((Unit _) => new InvalidOperationException());
                return error switch
                {
                    UpdateJobOfferStatusError.AlreadyInStatus =>
                        BadRequest(new { error = "Job offer is already in the requested status." }),
                    UpdateJobOfferStatusError.InvalidTransition =>
                        BadRequest(new { error = "The requested status transition is not allowed." }),
                };
            }

            stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
            await session.SaveChangesAsync(ct);

            return Ok(await LoadDetailAsync(id, ct));
        });
    }

    // ───── Add Comment ─────

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
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return NotFound();

        if (!AppUser.IsAdmin && offer.UserId != AppUser.Id)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Comment content is required." });

        var commentEvent = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: AppUser.Id,
            UserEmail: AppUser.Email,
            UserName: AppUser.DisplayName,
            Content: request.Content.Trim(),
            Timestamp: timeProvider.GetUtcNow());

        session.Events.Append(CommentStreamId.For(id), commentEvent);
        await session.SaveChangesAsync(ct);

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
    [Authorize]
    [ProducesResponseType<ListJobOffersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMine(
        [FromQuery] JobOfferStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return Ok(await ListOffersAsync(status, page, pageSize, ct));
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
        return Ok(await ListOffersAsync(status, page, pageSize, ct));
    }

    // ───── Get Detail ─────

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<GetJobOfferDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var detail = await LoadDetailAsync(id, ct);
        return detail == null ? NotFound() : Ok(detail);
    }

    // ───── History ─────

    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProducesResponseType<JobOfferHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return NotFound();

        if (!AppUser.IsAdmin && offer.UserId != AppUser.Id)
            return NotFound();

        var offerEvents = await session.Events.FetchStreamAsync(id, token: ct);
        var commentEvents = await session.Events.FetchStreamAsync(CommentStreamId.For(id), token: ct);

        var entries = offerEvents.Concat(commentEvents)
            .Select(e => e.Data switch
            {
                JobOfferSubmitted s => new JobOfferHistoryEntry(
                    EventType: "Submitted",
                    Description: "Job offer submitted",
                    ActorEmail: s.UserEmail,
                    Timestamp: s.Timestamp),
                JobOfferEdited ed => new JobOfferHistoryEntry(
                    EventType: "Edited",
                    Description: "Job offer edited",
                    ActorEmail: ed.EditedByEmail,
                    Timestamp: ed.Timestamp),
                JobOfferStatusChanged sc => new JobOfferHistoryEntry(
                    EventType: "StatusChanged",
                    Description: $"Status changed from {FormatStatus(sc.OldStatus)} to {FormatStatus(sc.NewStatus)}"
                        + (sc.Notes != null ? $" — {sc.Notes}" : ""),
                    ActorEmail: sc.ChangedByEmail,
                    Timestamp: sc.Timestamp),
                JobOfferCancelled c => new JobOfferHistoryEntry(
                    EventType: "Cancelled",
                    Description: "Job offer cancelled" + (c.Reason != null ? $" — {c.Reason}" : ""),
                    ActorEmail: c.CancelledByEmail,
                    Timestamp: c.Timestamp),
                JobOfferCommentAdded cm => new JobOfferHistoryEntry(
                    EventType: "Comment",
                    Description: cm.Content,
                    ActorEmail: cm.UserEmail,
                    Timestamp: cm.Timestamp),
                _ => new JobOfferHistoryEntry(
                    EventType: "Unknown",
                    Description: "Unknown event",
                    ActorEmail: "",
                    Timestamp: DateTimeOffset.MinValue)
            })
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Ok(new JobOfferHistoryResponse(entries));
    }

    // ───── List Comments ─────

    [HttpGet("{id:guid}/comments")]
    [Authorize]
    [ProducesResponseType<ListCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListComments(Guid id, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return NotFound();

        if (!AppUser.IsAdmin && offer.UserId != AppUser.Id)
            return NotFound();

        var events = await session.Events.FetchStreamAsync(CommentStreamId.For(id), token: ct);

        var comments = events
            .Select(e => (JobOfferCommentAdded)e.Data)
            .Select(c => new CommentResponse(
                Id: c.CommentId,
                UserId: c.UserId,
                UserEmail: c.UserEmail,
                UserName: c.UserName,
                Content: c.Content,
                CreatedAt: c.Timestamp))
            .ToList();

        return Ok(new ListCommentsResponse(comments));
    }

    // ───── Private helpers ─────

    private async Task<GetJobOfferDetailResponse?> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return null;

        if (!AppUser.IsAdmin && offer.UserId != AppUser.Id)
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
        JobOfferStatus? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        var query = session.Query<JobOffer>();

        if (!AppUser.IsAdmin)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.UserId == AppUser.Id);
        }

        if (status != null)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.Status == status);
        }

        var pagedResult = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobOfferSummary(
                Id: j.Id,
                CompanyName: j.CompanyName,
                JobTitle: j.JobTitle,
                ContactEmail: j.ContactEmail,
                Status: j.Status,
                IsRemote: j.IsRemote,
                Location: j.Location,
                CreatedAt: j.CreatedAt))
            .ToPagedListAsync(page, pageSize, ct);

        return new ListJobOffersResponse(pagedResult.ToList(), (int)pagedResult.TotalItemCount);
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

    private static string FormatStatus(JobOfferStatus status) => status switch
    {
        JobOfferStatus.Submitted => "Submitted",
        JobOfferStatus.InReview => "In Review",
        JobOfferStatus.Accepted => "Accepted",
        JobOfferStatus.Declined => "Declined",
        JobOfferStatus.Cancelled => "Cancelled",
    };
}
