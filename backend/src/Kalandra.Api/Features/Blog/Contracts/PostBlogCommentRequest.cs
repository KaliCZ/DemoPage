using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.Blog.Contracts;

public record PostBlogCommentRequest(
    [MaxLength(5000)] NonEmptyString Content,
    Guid? ParentCommentId);
