using System.ComponentModel.DataAnnotations;
using Kalandra.Api.StrongTypesExtensions;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CreateJobOfferRequest(
    [StringMaxLength(200)] NonEmptyString CompanyName,
    [StringMaxLength(200)] NonEmptyString ContactName,
    [EmailFormat, StringMaxLength(255)] NonEmptyString ContactEmail,
    [StringMaxLength(200)] NonEmptyString JobTitle,
    [StringMaxLength(5000)] NonEmptyString Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool IsRemote,
    [MaxLength(2000)] string? AdditionalNotes);
