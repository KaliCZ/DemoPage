using Kalandra.Blog.Entities;

namespace Kalandra.Api.Features.Blog.Contracts;

public record ToggleBlogReactionRequest(BlogReactionKind Kind);
