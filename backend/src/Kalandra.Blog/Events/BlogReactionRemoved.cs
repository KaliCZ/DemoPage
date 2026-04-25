using Kalandra.Blog.Entities;

namespace Kalandra.Blog.Events;

public record BlogReactionRemoved(
    string Slug,
    Guid UserId,
    BlogReactionEmoji Emoji,
    DateTimeOffset Timestamp);
