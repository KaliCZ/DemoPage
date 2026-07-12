using Kalandra.Blog.Entities;

namespace Kalandra.Blog.Events;

// VisitorId is the stable per-browser id; UserId is set once the reactor is signed in.
// Events that predate anonymous reactions carry only UserId and rehydrate with an empty
// VisitorId — harmless, because the aggregate keys on UserId whenever it is present.
public abstract record BlogReactionEvent(Guid VisitorId, Guid? UserId, BlogReactionKind Kind, DateTimeOffset Timestamp);

public sealed record BlogReactionAdded(Guid VisitorId, Guid? UserId, BlogReactionKind Kind, DateTimeOffset Timestamp)
    : BlogReactionEvent(VisitorId, UserId, Kind, Timestamp);

public sealed record BlogReactionRemoved(Guid VisitorId, Guid? UserId, BlogReactionKind Kind, DateTimeOffset Timestamp)
    : BlogReactionEvent(VisitorId, UserId, Kind, Timestamp);

/// <summary>Sign-in attribution: the visitor's anonymous reactions on this stream become the account's.</summary>
public sealed record BlogReactorLinked(Guid VisitorId, Guid UserId, DateTimeOffset Timestamp);
