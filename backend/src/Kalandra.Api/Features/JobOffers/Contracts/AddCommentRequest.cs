using Kalandra.Api.StrongTypesExtensions;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record AddCommentRequest([StringMaxLength(5000)] NonEmptyString Content);
