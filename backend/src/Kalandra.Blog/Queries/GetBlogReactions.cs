using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogReactionsQuery(string Slug, Guid? VisitorId, Guid? UserId);

/// <summary>A post's reaction counts plus the kinds the current reactor has active ("mine").</summary>
public record BlogReactionSummary(IReadOnlyDictionary<BlogReactionKind, int> CountsByKind, IReadOnlyCollection<BlogReactionKind> Mine)
{
    public int CountOf(BlogReactionKind kind) => CountsByKind.GetValueOrDefault(kind);

    public static BlogReactionSummary From(IReadOnlyList<BlogReaction> rows, Guid? visitorId, Guid? userId)
    {
        var countsByKind = rows.GroupBy(reaction => reaction.Kind).ToDictionary(group => group.Key, group => group.Count());
        // "Mine" is the account's reactions plus this browser's own not-yet-linked anonymous ones —
        // a signed-in reaction belongs to its account, never to whoever else shares the browser.
        var mine = rows
            .Where(reaction =>
                (userId is { } uid && reaction.UserId == uid)
                || (reaction.UserId is null && visitorId is { } vid && reaction.VisitorId == vid))
            .Select(reaction => reaction.Kind)
            .ToHashSet();
        return new BlogReactionSummary(countsByKind, mine);
    }
}

public class GetBlogReactionsHandler(IQuerySession session)
{
    public async Task<BlogReactionSummary> Get(GetBlogReactionsQuery query, CancellationToken ct)
    {
        var rows = await session.Query<BlogReaction>().Where(reaction => reaction.Slug == query.Slug).ToListAsync(ct);
        return BlogReactionSummary.From(rows, query.VisitorId, query.UserId);
    }
}
