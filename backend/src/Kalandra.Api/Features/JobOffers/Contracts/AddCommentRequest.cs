using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record AddCommentRequest([MaxLength(5000)] NonEmptyString Content, Guid? CommentId);
