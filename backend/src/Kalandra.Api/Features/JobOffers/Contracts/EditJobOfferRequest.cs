using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record EditJobOfferRequest(
    [Required, MaxLength(200)] string CompanyName,
    [Required, MaxLength(200)] string ContactName,
    [Required, EmailAddress, MaxLength(255)] string ContactEmail,
    [Required, MaxLength(200)] string JobTitle,
    [Required, MaxLength(5000)] string Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool IsRemote,
    [MaxLength(2000)] string? AdditionalNotes);
