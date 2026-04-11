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

        // Track current field state as we iterate the offer stream in append
        // order. When a JobOfferEdited event is encountered we diff its fields
        // against this state to produce a human-readable change summary.
        var companyName = "";
        var contactName = "";
        var contactEmail = "";
        var jobTitle = "";
        var description = "";
        string? salaryRange = null;
        string? location = null;
        var isRemote = false;
        string? additionalNotes = null;

        foreach (var evt in offerEvents)
        {
            switch (evt.Data)
            {
                case JobOfferSubmitted s:
                    companyName = s.CompanyName;
                    contactName = s.ContactName;
                    contactEmail = s.ContactEmail;
                    jobTitle = s.JobTitle;
                    description = s.Description;
                    salaryRange = s.SalaryRange;
                    location = s.Location;
                    isRemote = s.IsRemote;
                    additionalNotes = s.AdditionalNotes;
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "Submitted",
                        Description: "Job offer submitted",
                        ActorUserId: s.UserId,
                        ActorEmail: s.UserEmail,
                        Timestamp: s.Timestamp));
                    break;

                case JobOfferEdited ed:
                    var changes = new List<string>();
                    if (companyName != ed.CompanyName)
                        changes.Add($"company: {companyName} → {ed.CompanyName}");
                    if (jobTitle != ed.JobTitle)
                        changes.Add($"job title: {jobTitle} → {ed.JobTitle}");
                    if (contactName != ed.ContactName)
                        changes.Add($"contact name: {contactName} → {ed.ContactName}");
                    if (contactEmail != ed.ContactEmail)
                        changes.Add($"contact email: {contactEmail} → {ed.ContactEmail}");
                    if (location != ed.Location)
                        changes.Add($"location: {Display(location)} → {Display(ed.Location)}");
                    if (salaryRange != ed.SalaryRange)
                        changes.Add($"salary: {Display(salaryRange)} → {Display(ed.SalaryRange)}");
                    if (isRemote != ed.IsRemote)
                        changes.Add($"remote: {YesNo(isRemote)} → {YesNo(ed.IsRemote)}");
                    if (description != ed.Description)
                        changes.Add("description changed");
                    if (additionalNotes != ed.AdditionalNotes)
                        changes.Add("additional notes changed");

                    companyName = ed.CompanyName;
                    contactName = ed.ContactName;
                    contactEmail = ed.ContactEmail;
                    jobTitle = ed.JobTitle;
                    description = ed.Description;
                    salaryRange = ed.SalaryRange;
                    location = ed.Location;
                    isRemote = ed.IsRemote;
                    additionalNotes = ed.AdditionalNotes;

                    var editDescription = changes.Count > 0
                        ? "Edited — " + string.Join(", ", changes)
                        : "Job offer edited";
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "Edited",
                        Description: editDescription,
                        ActorUserId: ed.EditedByUserId,
                        ActorEmail: ed.EditedByEmail,
                        Timestamp: ed.Timestamp));
                    break;

                case JobOfferStatusChanged sc:
                    entries.Add(new JobOfferHistoryEntry(
                        EventType: "StatusChanged",
                        Description: $"Status changed from {FormatStatus(sc.OldStatus)} to {FormatStatus(sc.NewStatus)}"
                            + (sc.Notes != null ? $" — {sc.Notes}" : ""),
                        ActorUserId: sc.ChangedByUserId,
                        ActorEmail: sc.ChangedByEmail,
                        Timestamp: sc.Timestamp));
                    break;

                case JobOfferCancelled c:
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

    private static string Display(string? value) => string.IsNullOrEmpty(value) ? "—" : value;

    private static string YesNo(bool value) => value ? "yes" : "no";
}
