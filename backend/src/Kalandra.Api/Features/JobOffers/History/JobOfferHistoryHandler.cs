using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.History;

public class JobOfferHistoryHandler
{
    private readonly IQuerySession _session;

    public JobOfferHistoryHandler(IQuerySession session)
    {
        _session = session;
    }

    public async Task<JobOfferHistoryResponse?> HandleAsync(
        Guid id,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await _session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        var events = await _session.Events.FetchStreamAsync(id, token: ct);

        var entries = events.Select(e => e.Data switch
        {
            JobOfferSubmitted s => new JobOfferHistoryEntry(
                "Submitted", "Job offer submitted", s.UserEmail, s.Timestamp),
            JobOfferEdited ed => new JobOfferHistoryEntry(
                "Edited", "Job offer edited", ed.EditedByEmail, ed.Timestamp),
            JobOfferStatusChanged sc => new JobOfferHistoryEntry(
                "StatusChanged",
                $"Status changed from {FormatStatus(sc.OldStatus)} to {FormatStatus(sc.NewStatus)}"
                    + (sc.Notes != null ? $" — {sc.Notes}" : ""),
                sc.ChangedByEmail,
                sc.Timestamp),
            JobOfferCancelled c => new JobOfferHistoryEntry(
                "Cancelled",
                "Job offer cancelled" + (c.Reason != null ? $" — {c.Reason}" : ""),
                c.CancelledByEmail,
                c.Timestamp),
            JobOfferCommentAdded cm => new JobOfferHistoryEntry(
                "Comment",
                cm.Content,
                cm.UserEmail,
                cm.Timestamp),
            _ => new JobOfferHistoryEntry("Unknown", "Unknown event", "", DateTimeOffset.MinValue)
        }).ToList();

        return new JobOfferHistoryResponse(entries);
    }

    private static string FormatStatus(JobOfferStatus status) => status switch
    {
        JobOfferStatus.Submitted => "Submitted",
        JobOfferStatus.InReview => "In Review",
        JobOfferStatus.Accepted => "Accepted",
        JobOfferStatus.Declined => "Declined",
        JobOfferStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };
}
