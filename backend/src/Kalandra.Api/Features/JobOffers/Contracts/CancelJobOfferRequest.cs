using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CancelJobOfferRequest([MaxLength(2000)] string? Reason);
