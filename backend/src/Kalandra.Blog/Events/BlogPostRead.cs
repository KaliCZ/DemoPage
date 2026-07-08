namespace Kalandra.Blog.Events;

/// <summary>One signed-in view of a blog post — a user's read count is the number of these carrying their id.</summary>
public record BlogPostRead(Guid UserId, DateTimeOffset Timestamp);
