namespace Kalandra.Blog.Notifications;

/// <summary>
/// One row per delivered notification email, keyed by a deterministic id. The subscription commits it
/// in its own transaction right after a successful send, so a retried page — the daemon replays a whole
/// page when any send in it throws — skips the emails already delivered instead of re-sending them.
/// </summary>
public record BlogNotificationSent(string Id, DateTimeOffset SentAtUtc);
