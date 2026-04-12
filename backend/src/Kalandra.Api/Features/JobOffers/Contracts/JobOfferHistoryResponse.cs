using System.Text.Json.Serialization;
using Kalandra.JobOffers.Queries;

namespace Kalandra.Api.Features.JobOffers.Contracts;

/// <summary>
/// Stable API contract for field identifiers in edit history entries.
/// Separate from the domain <see cref="JobOfferField"/> enum so that
/// internal renames don't silently break frontend i18n keys.
/// </summary>
public enum HistoryField
{
    [JsonStringEnumMemberName("companyName")]
    CompanyName,

    [JsonStringEnumMemberName("jobTitle")]
    JobTitle,

    [JsonStringEnumMemberName("contactName")]
    ContactName,

    [JsonStringEnumMemberName("contactEmail")]
    ContactEmail,

    [JsonStringEnumMemberName("location")]
    Location,

    [JsonStringEnumMemberName("salaryRange")]
    SalaryRange,

    [JsonStringEnumMemberName("isRemote")]
    IsRemote,

    [JsonStringEnumMemberName("description")]
    Description,

    [JsonStringEnumMemberName("additionalNotes")]
    AdditionalNotes,
}

public record FieldChangeResponse(HistoryField Field, string? OldValue, string? NewValue);

public record HistoryEntryResponse(
    string EventType,
    string Description,
    Guid ActorUserId,
    string ActorEmail,
    DateTimeOffset Timestamp,
    IEnumerable<FieldChangeResponse>? Changes)
{
    public static HistoryEntryResponse Serialize(JobOfferHistoryEntry entry) => new(
        EventType: entry.EventType,
        Description: entry.Description,
        ActorUserId: entry.ActorUserId,
        ActorEmail: entry.ActorEmail,
        Timestamp: entry.Timestamp,
        Changes: entry.Changes?.Select(c => new FieldChangeResponse(
            MapField(c.Field), c.OldValue, c.NewValue)));

    private static HistoryField MapField(JobOfferField field) => field switch
    {
        JobOfferField.CompanyName => HistoryField.CompanyName,
        JobOfferField.JobTitle => HistoryField.JobTitle,
        JobOfferField.ContactName => HistoryField.ContactName,
        JobOfferField.ContactEmail => HistoryField.ContactEmail,
        JobOfferField.Location => HistoryField.Location,
        JobOfferField.SalaryRange => HistoryField.SalaryRange,
        JobOfferField.IsRemote => HistoryField.IsRemote,
        JobOfferField.Description => HistoryField.Description,
        JobOfferField.AdditionalNotes => HistoryField.AdditionalNotes,
    };
}

public record JobOfferHistoryResponse(
    IEnumerable<HistoryEntryResponse> Entries);
