using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public record CancelJobOfferRequest([MaxLength(2000)] string? Reason);
