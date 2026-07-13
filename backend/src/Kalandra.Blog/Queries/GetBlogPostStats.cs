using Npgsql;
using NpgsqlTypes;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

/// <summary>
/// Reads the blog-index stats with three set-based aggregate queries — one per source table — for the
/// whole requested batch, instead of a per-post loop that summed in the app. Marten's LINQ can't express
/// batched GROUP BY / COUNT(DISTINCT), so these drop to SQL against Marten's own storage tables; the
/// counts stay in Postgres and only the totals come back.
/// </summary>
public class GetBlogPostStatsHandler(NpgsqlDataSource dataSource)
{
    // total_views sums a JSON field (ViewCount isn't a duplicated column); unique visitors is the same
    // "userId ?? visitorId" identity as reactions, de-duped in SQL rather than materialized in memory.
    private const string ViewStatsSql = """
        SELECT slug,
               COALESCE(SUM((data ->> 'ViewCount')::int), 0)::int                                  AS total_views,
               COUNT(DISTINCT COALESCE(user_id, visitor_id))::int                                  AS unique_visitors,
               COALESCE(SUM((data ->> 'ViewCount')::int) FILTER (WHERE user_id = @viewer), 0)::int  AS viewer_views
        FROM public.mt_doc_blogpostvisitorview
        WHERE slug = ANY(@slugs)
        GROUP BY slug;
        """;

    private const string ReactionStatsSql = """
        SELECT slug, COUNT(*)::int AS total_reactions
        FROM public.mt_doc_blogreaction
        WHERE slug = ANY(@slugs)
        GROUP BY slug;
        """;

    // Comments are events, not a countable table: the live count is posts minus deletes (tombstones) per stream.
    private const string CommentStatsSql = """
        SELECT stream_id,
               (COUNT(*) FILTER (WHERE type = 'blog_comment_posted')
              - COUNT(*) FILTER (WHERE type = 'blog_comment_deleted'))::int AS total_comments
        FROM public.mt_events
        WHERE stream_id = ANY(@streamIds)
        GROUP BY stream_id;
        """;

    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        if (query.Posts.Count == 0)
            return [];

        var slugs = query.Posts.Select(post => post.Slug).ToArray();
        var streamIds = query.Posts.Select(post => post.CommentsStreamId).ToArray();

        // The three aggregates hit independent tables on their own connections, so run them at once
        // and pay the slowest rather than the sum.
        var viewsTask = ReadViewStatsAsync(slugs, query.ViewerId, ct);
        var reactionsTask = ReadReactionCountsAsync(slugs, ct);
        var commentsTask = ReadCommentCountsAsync(streamIds, ct);
        await Task.WhenAll(viewsTask, reactionsTask, commentsTask);
        var views = await viewsTask;
        var reactions = await reactionsTask;
        var comments = await commentsTask;

        return
        [
            .. query.Posts.Select(post =>
            {
                var view = views.GetValueOrDefault(post.Slug);
                return new BlogPostStats(
                    Slug: post.Slug,
                    TotalViews: view.TotalViews,
                    UniqueVisitors: view.UniqueVisitors,
                    TotalReactions: reactions.GetValueOrDefault(post.Slug),
                    TotalComments: comments.GetValueOrDefault(post.CommentsStreamId),
                    ViewerViews: query.ViewerId is null ? null : view.ViewerViews);
            }),
        ];
    }

    private readonly record struct ViewStats(int TotalViews, int UniqueVisitors, int ViewerViews);

    private async Task<Dictionary<string, ViewStats>> ReadViewStatsAsync(string[] slugs, Guid? viewerId, CancellationToken ct)
    {
        var command = dataSource.CreateCommand(ViewStatsSql);
        command.Parameters.Add(new NpgsqlParameter("slugs", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = slugs });
        command.Parameters.Add(new NpgsqlParameter("viewer", NpgsqlDbType.Uuid) { Value = (object?)viewerId ?? DBNull.Value });

        var result = new Dictionary<string, ViewStats>(StringComparer.Ordinal);
        await ReadRowsAsync(command, reader =>
            result[reader.GetString(0)] = new ViewStats(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)), ct);
        return result;
    }

    private async Task<Dictionary<string, int>> ReadReactionCountsAsync(string[] slugs, CancellationToken ct)
    {
        var command = dataSource.CreateCommand(ReactionStatsSql);
        command.Parameters.Add(new NpgsqlParameter("slugs", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = slugs });

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await ReadRowsAsync(command, reader => result[reader.GetString(0)] = reader.GetInt32(1), ct);
        return result;
    }

    private async Task<Dictionary<Guid, int>> ReadCommentCountsAsync(Guid[] streamIds, CancellationToken ct)
    {
        var command = dataSource.CreateCommand(CommentStatsSql);
        command.Parameters.Add(new NpgsqlParameter("streamIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = streamIds });

        var result = new Dictionary<Guid, int>();
        await ReadRowsAsync(command, reader => result[reader.GetGuid(0)] = reader.GetInt32(1), ct);
        return result;
    }

    private static async Task ReadRowsAsync(NpgsqlCommand command, Action<NpgsqlDataReader> onRow, CancellationToken ct)
    {
        try
        {
            await using var _ = command;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                onRow(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Marten creates each mt_doc_/mt_events table lazily on first write; until then it's absent, which reads as zero rows.
        }
    }
}
