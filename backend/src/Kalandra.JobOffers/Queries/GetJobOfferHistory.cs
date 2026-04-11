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

        var entries = new List<JobOfferHistoryEntry>();

        // Replay the stream through a fresh aggregate so state reconstruction
        // goes through the real JobOffer.Apply() methods — this keeps the
        // history handler from having its own shadow copy of the apply logic.
        // For JobOfferEdited we snapshot fields before and after Apply() and
        // diff the two to describe what actually changed.
        var replay = new JobOffer();

        foreach (var evt in offerEvents)
        {
            switch (evt.Data)
            {
                case JobOfferSubmitted s:
                    replay.Apply(s);
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "Submitted",
                        Description: "Job offer submitted",
                        ActorUserId: s.UserId,
                        ActorEmail: s.UserEmail,
                        Timestamp: s.Timestamp));
                    break;

                case JobOfferEdited ed:
                    var before = FieldSnapshot.From(replay);
                    replay.Apply(ed);
                    var after = FieldSnapshot.From(replay);
                    var changes = FieldSnapshot.Diff(before, after);

                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "Edited",
                        Description: changes.Count > 0
                            ? "Edited — " + string.Join(", ", changes)
                            : "Job offer edited",
                        ActorUserId: ed.EditedByUserId,
                        ActorEmail: ed.EditedByEmail,
                        Timestamp: ed.Timestamp));
                    break;

                case JobOfferStatusChanged sc:
                    replay.Apply(sc);
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "StatusChanged",
                        Description: $"Status changed from {FormatStatus(sc.OldStatus)} to {FormatStatus(sc.NewStatus)}"
                            + (sc.Notes != null ? $" — {sc.Notes}" : ""),
                        ActorUserId: sc.ChangedByUserId,
                        ActorEmail: sc.ChangedByEmail,
                        Timestamp: sc.Timestamp));
                    break;

                case JobOfferCancelled c:
                    replay.Apply(c);
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "Cancelled",
                        Description: "Job offer cancelled" + (c.Reason != null ? $" — {c.Reason}" : ""),
                        ActorUserId: c.CancelledByUserId,
                        ActorEmail: c.CancelledByEmail,
                        Timestamp: c.Timestamp));
                    break;
            }
        }

        // Comments live on a separate stream and don't affect field state.
        foreach (var evt in commentEvents)
        {
            if (evt.Data is JobOfferCommentAdded cm)
            {
                entries.Add(new JobOfferHistoryEntry(
                    EventType: "Comment",
                    Description: cm.Content,
                    ActorUserId: cm.UserId,
                    ActorEmail: cm.UserEmail,
                    Timestamp: cm.Timestamp));
            }
        }

        return entries.OrderBy(e => e.Timestamp).ToList();
    }

    private static string FormatStatus(JobOfferStatus status) => status switch
    {
        JobOfferStatus.Submitted => "Submitted",
        JobOfferStatus.InReview => "In Review",
        JobOfferStatus.LetsTalk => "Let's Talk",
        JobOfferStatus.Declined => "Declined",
        JobOfferStatus.Cancelled => "Cancelled",
    };

    /// <summary>
    /// Immutable snapshot of the fields that can change via JobOfferEdited.
    /// Taken before and after Apply() so we can diff the two without having
    /// to mirror the aggregate's apply logic in this handler.
    /// </summary>
    private record FieldSnapshot(
        string CompanyName,
        string ContactName,
        string ContactEmail,
        string JobTitle,
        string Description,
        string? SalaryRange,
        string? Location,
        bool IsRemote,
        string? AdditionalNotes)
    {
        public static FieldSnapshot From(JobOffer offer) => new(
            CompanyName: offer.CompanyName,
            ContactName: offer.ContactName,
            ContactEmail: offer.ContactEmail,
            JobTitle: offer.JobTitle,
            Description: offer.Description,
            SalaryRange: offer.SalaryRange,
            Location: offer.Location,
            IsRemote: offer.IsRemote,
            AdditionalNotes: offer.AdditionalNotes);

        public static List<string> Diff(FieldSnapshot before, FieldSnapshot after)
        {
            var changes = new List<string>();
            if (before.CompanyName != after.CompanyName)
                changes.Add($"company: {before.CompanyName} → {after.CompanyName}");
            if (before.JobTitle != after.JobTitle)
                changes.Add($"job title: {before.JobTitle} → {after.JobTitle}");
            if (before.ContactName != after.ContactName)
                changes.Add($"contact name: {before.ContactName} → {after.ContactName}");
            if (before.ContactEmail != after.ContactEmail)
                changes.Add($"contact email: {before.ContactEmail} → {after.ContactEmail}");
            if (before.Location != after.Location)
                changes.Add($"location: {Display(before.Location)} → {Display(after.Location)}");
            if (before.SalaryRange != after.SalaryRange)
                changes.Add($"salary: {Display(before.SalaryRange)} → {Display(after.SalaryRange)}");
            if (before.IsRemote != after.IsRemote)
                changes.Add($"remote: {YesNo(before.IsRemote)} → {YesNo(after.IsRemote)}");
            if (before.Description != after.Description)
                changes.Add("description changed");
            if (before.AdditionalNotes != after.AdditionalNotes)
                changes.Add("additional notes changed");
            return changes;
        }

        private static string Display(string? value) => string.IsNullOrEmpty(value) ? "—" : value;
        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
