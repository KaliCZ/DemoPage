using Kalandra.Blog.Entities;

namespace Kalandra.Blog.Events;

public record BlogReactionAdded(
    string Slug,
    Guid UserId,
    BlogReactionEmoji Emoji,
    DateTimeOffset Timestamp);
