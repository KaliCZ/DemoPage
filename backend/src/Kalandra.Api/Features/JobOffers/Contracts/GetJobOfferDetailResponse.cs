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
    string UserEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static GetJobOfferDetailResponse Serialize(JobOffer offer) => new(
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
        UserEmail: offer.UserEmail,
        CreatedAt: offer.CreatedAt,
        UpdatedAt: offer.UpdatedAt);
}
