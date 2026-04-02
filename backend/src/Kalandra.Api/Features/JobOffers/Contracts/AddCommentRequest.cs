using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record AddCommentRequest([Required, MaxLength(5000)] string Content);
