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

        // We walk the offer stream in append order and maintain the current
        // field state as each event is processed. When a JobOfferEdited event
        // is encountered we diff the fields *it actually changed* (non-null
        // fields in the event — null means "not edited") against the running
        // state built from the Submitted event and every prior edit. After
        // building the diff we advance the state so subsequent edits compare
        // against the correct baseline.
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
                    if (ed.CompanyName != null && ed.CompanyName != companyName)
                    {
                        changes.Add($"company: {companyName} → {ed.CompanyName}");
                        companyName = ed.CompanyName;
                    }
                    if (ed.JobTitle != null && ed.JobTitle != jobTitle)
                    {
                        changes.Add($"job title: {jobTitle} → {ed.JobTitle}");
                        jobTitle = ed.JobTitle;
                    }
                    if (ed.ContactName != null && ed.ContactName != contactName)
                    {
                        changes.Add($"contact name: {contactName} → {ed.ContactName}");
                        contactName = ed.ContactName;
                    }
                    if (ed.ContactEmail != null && ed.ContactEmail != contactEmail)
                    {
                        changes.Add($"contact email: {contactEmail} → {ed.ContactEmail}");
                        contactEmail = ed.ContactEmail;
                    }
                    if (ed.Location != null && ed.Location != location)
                    {
                        changes.Add($"location: {Display(location)} → {ed.Location}");
                        location = ed.Location;
                    }
                    if (ed.SalaryRange != null && ed.SalaryRange != salaryRange)
                    {
                        changes.Add($"salary: {Display(salaryRange)} → {ed.SalaryRange}");
                        salaryRange = ed.SalaryRange;
                    }
                    if (ed.IsRemote.HasValue && ed.IsRemote.Value != isRemote)
                    {
                        changes.Add($"remote: {YesNo(isRemote)} → {YesNo(ed.IsRemote.Value)}");
                        isRemote = ed.IsRemote.Value;
                    }
                    if (ed.Description != null && ed.Description != description)
                    {
                        changes.Add("description changed");
                        description = ed.Description;
                    }
                    if (ed.AdditionalNotes != null && ed.AdditionalNotes != additionalNotes)
                    {
                        changes.Add("additional notes changed");
                        additionalNotes = ed.AdditionalNotes;
                    }

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
