using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.History;

public class JobOfferHistoryHandler(IQuerySession session)
{
    public async Task<JobOfferHistoryResponse?> HandleAsync(
        Guid id,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        var offerEvents = await session.Events.FetchStreamAsync(id, token: ct);
        var commentStreamId = AddCommentHandler.CommentStreamId(id);
        var commentEvents = await session.Events.FetchStreamAsync(commentStreamId, token: ct);

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

        return new JobOfferHistoryResponse(entries);
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
