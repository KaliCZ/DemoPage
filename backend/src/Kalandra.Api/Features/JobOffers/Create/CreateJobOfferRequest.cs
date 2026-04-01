using System.ComponentModel.DataAnnotations;
using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Create;

public record CreateJobOfferRequest(
    Guid? Id,
    [Required, MaxLength(200)] string CompanyName,
    [Required, MaxLength(200)] string ContactName,
    [Required, EmailAddress, MaxLength(255)] string ContactEmail,
    [Required, MaxLength(200)] string JobTitle,
    [Required, MaxLength(5000)] string Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool IsRemote,
    [MaxLength(2000)] string? AdditionalNotes,
    IReadOnlyList<AttachmentInfo>? Attachments) : IValidatableObject
{
    private const int MaxAttachments = 5;
    private const long MaxTotalBytes = 15 * 1024 * 1024; // 15 MB

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Attachments is not { Count: > 0 })
            yield break;

        if (Id is null || Id == Guid.Empty)
            yield return new ValidationResult(
                "An offer ID is required when attachments are included.",
                [nameof(Id)]);

        if (Attachments.Count > MaxAttachments)
            yield return new ValidationResult(
                $"Maximum {MaxAttachments} attachments allowed.",
                [nameof(Attachments)]);

        if (Attachments.Sum(a => a.FileSize) > MaxTotalBytes)
            yield return new ValidationResult(
                "Total attachment size must not exceed 15 MB.",
                [nameof(Attachments)]);
    }
}
