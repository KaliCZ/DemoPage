using System.ComponentModel.DataAnnotations;
using StrongTypes;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record EditJobOfferRequest(
    [MaxLength(200)] NonEmptyString? CompanyName,
    [MaxLength(200)] NonEmptyString? ContactName,
    Email? ContactEmail,
    [MaxLength(200)] NonEmptyString? JobTitle,
    [MaxLength(5000)] NonEmptyString? Description,
    [MaxLength(100)] string? SalaryRange,
    [MaxLength(200)] string? Location,
    bool? IsRemote,
    [MaxLength(2000)] string? AdditionalNotes);
