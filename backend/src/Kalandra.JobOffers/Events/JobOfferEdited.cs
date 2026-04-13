namespace Kalandra.JobOffers.Events;

/// <summary>
/// Represents an edit to a job offer. Null for a field means the field was
/// NOT changed by this edit — it should be skipped by projections and by the
/// activity log. Only fields with a non-null value were actually modified.
/// </summary>
public record JobOfferEdited(
    Guid EditedByUserId,
    string EditedByEmail,
    string? CompanyName,
    string? ContactName,
    string? ContactEmail,
    string? JobTitle,
    string? Description,
    string? SalaryRange,
    string? Location,
    bool? IsRemote,
    string? AdditionalNotes,
    DateTimeOffset Timestamp);
