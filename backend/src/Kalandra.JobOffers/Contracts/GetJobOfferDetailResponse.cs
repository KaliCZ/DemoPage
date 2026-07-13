using Kalandra.JobOffers.Entities;
using StrongTypes;

namespace Kalandra.JobOffers.Contracts;

public record GetJobOfferDetailResponse(
    Guid Id,
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    Email ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    IReadOnlyList<AttachmentInfo> Attachments,
    JobOfferStatus Status,
    Email UserEmail,
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
