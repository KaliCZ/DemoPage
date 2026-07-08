using Kalandra.Blog.Events;

namespace Kalandra.Blog.Entities;

/// <summary>
/// Per-post read counters. Unlike reactions this is snapshotted inline — the stream
/// gains an event per page view, so reads must not replay it (see ConfigureBlog).
/// </summary>
public class BlogPostReads
{
    public Guid Id { get; set; }
    public int TotalReads { get; set; }
    public Dictionary<Guid, int> ReadCountsByUser { get; set; } = [];

    public int CountFor(Guid userId) => ReadCountsByUser.GetValueOrDefault(userId);

    public void Apply(BlogPostRead e)
    {
        TotalReads++;
        ReadCountsByUser[e.UserId] = CountFor(e.UserId) + 1;
    }
}
