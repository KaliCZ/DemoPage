using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;

namespace Kalandra.JobOffers.Queries;

public record GetJobOfferHistoryQuery(Guid Id, CurrentUser User);

public record JobOfferHistoryEntry(
    string EventType,
    string Description,
    Guid ActorUserId,
    string ActorEmail,
    DateTimeOffset Timestamp);

public class GetJobOfferHistoryHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<JobOfferHistoryEntry>?> HandleAsync(
        GetJobOfferHistoryQuery query, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(query.Id, ct);
        if (offer == null)
            return null;

        if (!query.User.IsAdmin && offer.UserId != query.User.Id)
            return null;

        var offerEvents = await session.Events.FetchStreamAsync(query.Id, token: ct);
        var commentEvents = await session.Events.FetchStreamAsync(CommentStreamId.For(query.Id), token: ct);

        return offerEvents.Concat(commentEvents)
            .Select(e => e.Data switch
            {
                JobOfferSubmitted s => new JobOfferHistoryEntry(
                    EventType: "Submitted",
                    Description: "Job offer submitted",
                    ActorUserId: s.UserId,
                    ActorEmail: s.UserEmail,
                    Timestamp: s.Timestamp),
                JobOfferEdited ed => new JobOfferHistoryEntry(
                    EventType: "Edited",
                    Description: "Job offer edited",
                    ActorUserId: ed.EditedByUserId,
                    ActorEmail: ed.EditedByEmail,
                    Timestamp: ed.Timestamp),
                JobOfferStatusChanged sc => new JobOfferHistoryEntry(
                    EventType: "StatusChanged",
                    Description: $"Status changed from {FormatStatus(sc.OldStatus)} to {FormatStatus(sc.NewStatus)}"
                        + (sc.Notes != null ? $" — {sc.Notes}" : ""),
                    ActorUserId: sc.ChangedByUserId,
                    ActorEmail: sc.ChangedByEmail,
                    Timestamp: sc.Timestamp),
                JobOfferCancelled c => new JobOfferHistoryEntry(
                    EventType: "Cancelled",
                    Description: "Job offer cancelled" + (c.Reason != null ? $" — {c.Reason}" : ""),
                    ActorUserId: c.CancelledByUserId,
                    ActorEmail: c.CancelledByEmail,
                    Timestamp: c.Timestamp),
                JobOfferCommentAdded cm => new JobOfferHistoryEntry(
                    EventType: "Comment",
                    Description: cm.Content,
                    ActorUserId: cm.UserId,
                    ActorEmail: cm.UserEmail,
                    Timestamp: cm.Timestamp),
                _ => new JobOfferHistoryEntry(
                    EventType: "Unknown",
                    Description: "Unknown event",
                    ActorUserId: Guid.Empty,
                    ActorEmail: "",
                    Timestamp: DateTimeOffset.MinValue)
            })
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    private static string FormatStatus(JobOfferStatus status) => status switch
    {
        JobOfferStatus.Submitted => "Submitted",
        JobOfferStatus.InReview => "In Review",
        JobOfferStatus.LetsTalk => "Let's Talk",
        JobOfferStatus.Declined => "Declined",
        JobOfferStatus.Cancelled => "Cancelled",
    };
}
