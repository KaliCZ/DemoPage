using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.Blog.Contracts;

// Content emptiness/trim is enforced inside the controller so the error code on
// the wire is the stable enum name (`ContentRequired`), not the framework's
// generic `[Required]` message. The MaxLength guard stays — it's a hard cap
// on the stream payload, not part of the i18n error contract.
public record AddBlogCommentRequest([MaxLength(5000)] string Content);
