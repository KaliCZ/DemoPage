using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.Blog.Contracts;

// Content is a loose string on purpose: the controller trims and re-validates it
// so missing/empty/whitespace input surfaces as the stable ContentRequired code
// instead of a converter failure with no i18n key.
public record PostBlogCommentRequest(
    [MaxLength(5000)] string? Content,
    Guid? ParentCommentId);
