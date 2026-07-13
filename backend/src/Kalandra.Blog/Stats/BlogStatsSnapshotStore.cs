using Npgsql;
using NpgsqlTypes;

namespace Kalandra.Blog.Stats;

/// <summary>
/// Persists and reads the blog stats snapshot. Each metric is its own real column and is written by
/// its own statement, so independent refreshers updating different metrics never clobber one
/// another the way concurrent writes to a single JSON document would. Reads are a by-id lookup, cheap
/// enough for a small database.
/// </summary>
public class BlogStatsSnapshotStore(NpgsqlDataSource dataSource)
{
    // Marten's default schema; the app never overrides DatabaseSchemaName, so the snapshot sits
    // alongside the event and document tables.
    private const string Table = "public.blog_post_stats_snapshot";

    private const string CreateTableSql = $"""
        CREATE TABLE IF NOT EXISTS {Table} (
            slug             text PRIMARY KEY,
            total_views      integer     NOT NULL DEFAULT 0,
            unique_visitors  integer     NOT NULL DEFAULT 0,
            total_reactions  integer     NOT NULL DEFAULT 0,
            total_comments   integer     NOT NULL DEFAULT 0,
            refreshed_at_utc timestamptz NOT NULL DEFAULT now()
        );
        """;

    private const string LoadSql = $"""
        SELECT slug, total_views, unique_visitors, total_reactions, total_comments, refreshed_at_utc
        FROM {Table}
        WHERE slug = ANY(@slugs);
        """;

    private volatile bool tableEnsured;
    private readonly SemaphoreSlim ensureLock = new(1, 1);

    /// <summary>Creates the snapshot table if absent, once per process. Callers on the write path invoke it before upserting; reads tolerate its absence instead.</summary>
    public async Task EnsureTableAsync(CancellationToken ct)
    {
        if (tableEnsured)
            return;

        await ensureLock.WaitAsync(ct);
        try
        {
            if (tableEnsured)
                return;
            await using var command = dataSource.CreateCommand(CreateTableSql);
            await command.ExecuteNonQueryAsync(ct);
            tableEnsured = true;
        }
        finally
        {
            ensureLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, BlogPostStatsSnapshot>> LoadAsync(
        IReadOnlyCollection<string> slugs, CancellationToken ct)
    {
        var result = new Dictionary<string, BlogPostStatsSnapshot>(StringComparer.Ordinal);
        if (slugs.Count == 0)
            return result;

        try
        {
            await using var command = dataSource.CreateCommand(LoadSql);
            command.Parameters.Add(new NpgsqlParameter("slugs", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = slugs.ToArray() });

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var slug = reader.GetString(0);
                result[slug] = new BlogPostStatsSnapshot(
                    Slug: slug,
                    TotalViews: reader.GetInt32(1),
                    UniqueVisitors: reader.GetInt32(2),
                    TotalReactions: reader.GetInt32(3),
                    TotalComments: reader.GetInt32(4),
                    RefreshedAtUtc: reader.GetFieldValue<DateTimeOffset>(5));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Not materialized yet (a fresh deploy before the first refresh) — the index reads as zeros until then.
        }

        return result;
    }

    // Each setter is a distinct async mechanism's write: it touches only its own metric column (plus
    // the shared refresh timestamp), so the four can run independently without losing each other's value.
    public Task SetTotalViewsAsync(string slug, int value, CancellationToken ct) =>
        UpsertAsync($"INSERT INTO {Table} (slug, total_views) VALUES (@slug, @value) ON CONFLICT (slug) DO UPDATE SET total_views = @value, refreshed_at_utc = now();", slug, value, ct);

    public Task SetUniqueVisitorsAsync(string slug, int value, CancellationToken ct) =>
        UpsertAsync($"INSERT INTO {Table} (slug, unique_visitors) VALUES (@slug, @value) ON CONFLICT (slug) DO UPDATE SET unique_visitors = @value, refreshed_at_utc = now();", slug, value, ct);

    public Task SetTotalReactionsAsync(string slug, int value, CancellationToken ct) =>
        UpsertAsync($"INSERT INTO {Table} (slug, total_reactions) VALUES (@slug, @value) ON CONFLICT (slug) DO UPDATE SET total_reactions = @value, refreshed_at_utc = now();", slug, value, ct);

    public Task SetTotalCommentsAsync(string slug, int value, CancellationToken ct) =>
        UpsertAsync($"INSERT INTO {Table} (slug, total_comments) VALUES (@slug, @value) ON CONFLICT (slug) DO UPDATE SET total_comments = @value, refreshed_at_utc = now();", slug, value, ct);

    private async Task UpsertAsync(string sql, string slug, int value, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("slug", NpgsqlDbType.Text) { Value = slug });
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Integer) { Value = value });
        await command.ExecuteNonQueryAsync(ct);
    }
}
