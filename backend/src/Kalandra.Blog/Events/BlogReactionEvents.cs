using Kalandra.Blog.Entities;

namespace Kalandra.Blog.Events;

public abstract record BlogReactionEvent(Guid UserId, BlogReactionKind Kind, DateTimeOffset Timestamp);

public sealed record BlogReactionAdded(Guid UserId, BlogReactionKind Kind, DateTimeOffset Timestamp)
    : BlogReactionEvent(UserId, Kind, Timestamp);

public sealed record BlogReactionRemoved(Guid UserId, BlogReactionKind Kind, DateTimeOffset Timestamp)
    : BlogReactionEvent(UserId, Kind, Timestamp);
