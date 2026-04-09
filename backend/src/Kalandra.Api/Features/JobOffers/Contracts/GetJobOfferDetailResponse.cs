using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record GetJobOfferDetailResponse(
    Guid Id,
    string CompanyName,
    string ContactName,
    string ContactEmail,
    string JobTitle,
    string Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    IReadOnlyList<AttachmentInfo> Attachments,
    JobOfferStatus Status,
    string? AdminNotes,
    string UserEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Projects a JobOffer aggregate to the detail response. AdminNotes are
    /// only exposed to admins — the viewing user is required for that gate.
    /// </summary>
    public static GetJobOfferDetailResponse Serialize(JobOffer offer, CurrentUser viewer) => new(
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
        AdminNotes: viewer.IsAdmin ? offer.AdminNotes : null,
        UserEmail: offer.UserEmail,
        CreatedAt: offer.CreatedAt,
        UpdatedAt: offer.UpdatedAt);
}
