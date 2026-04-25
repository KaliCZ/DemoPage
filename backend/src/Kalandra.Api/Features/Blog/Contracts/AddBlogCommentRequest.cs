using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.Blog.Contracts;

public record AddBlogCommentRequest([Required, MaxLength(5000)] string Content);
