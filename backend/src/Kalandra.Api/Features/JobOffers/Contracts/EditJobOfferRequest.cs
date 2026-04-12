using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record EditJobOfferRequest(
    [MaxLength(200)] string? CompanyName,
    [MaxLength(200)] string? ContactName,
    [EmailAddress, MaxLength(255)] string? ContactEmail,
    [MaxLength(200)] string? JobTitle,
    [MaxLength(5000)] string? Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool? IsRemote,
    [MaxLength(2000)] string? AdditionalNotes);
