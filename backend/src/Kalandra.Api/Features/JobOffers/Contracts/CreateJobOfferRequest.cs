using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CreateJobOfferRequest(
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    NonEmptyString ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool IsRemote,
    [MaxLength(2000)] string? AdditionalNotes);
