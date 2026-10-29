namespace Kalandra.Blog.Notifications;

/// <summary>
/// Committed right after each send, because the daemon replays a whole page when any send in it throws.
/// </summary>
public record BlogNotificationSent(string Id, DateTimeOffset SentAtUtc);
