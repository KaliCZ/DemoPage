namespace Kalandra.Blog.Stats;

/// <summary>
/// The precomputed public totals for one post, one row per slug (the primary key). Read by the blog
/// index so the stats endpoint is a by-id lookup instead of a live aggregate over every view,
/// reaction and comment.
/// </summary>
public record BlogPostStatsSnapshot(
    string Slug,
    int TotalViews,
    int UniqueVisitors,
    int TotalReactions,
    int TotalComments,
    DateTimeOffset RefreshedAtUtc);
