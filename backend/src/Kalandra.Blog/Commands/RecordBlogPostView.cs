using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Commands;

public record RecordBlogPostViewCommand(string Slug, Guid VisitorId, Guid? UserId, DateTimeOffset NowUtc);

/// <summary>
/// PreviousViewCount is the reader's own visits before this one; TotalViews is the post's
/// public total (page views); UniqueVisitors is how many distinct people it reached.
/// </summary>
public record RecordBlogPostViewResult(int PreviousViewCount, int TotalViews, int UniqueVisitors);

/// <summary>The visitor id's view row already belongs to a different account, so it can't be reused.</summary>
public enum RecordBlogPostViewError
{
    VisitorClaimedByAnotherUser,
}

public class RecordBlogPostViewHandler(IDocumentSession session)
{
    // A refresh inside this window is the same visit; a return after it is a new view.
    public static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Records the visit and returns the reader's own count before it (signed-in readers get their
    /// cross-device total, anonymous readers just this browser's) plus the post's totals. Fails with
    /// VisitorClaimedByAnotherUser when the visitor id already belongs to a different account, so the
    /// caller mints a fresh id instead of landing this view on someone else's row.
    /// </summary>
    public async Task<Result<RecordBlogPostViewResult, RecordBlogPostViewError>> RecordAndSave(
        RecordBlogPostViewCommand command, CancellationToken ct)
    {
        var id = BlogPostVisitorView.IdFor(command.Slug, command.VisitorId);
        var view = await session.LoadAsync<BlogPostVisitorView>(id, ct);

        // A row owned by another account means this visitor id is shared; refuse rather than count
        // the visit against someone else's row or echo back their private total.
        if (view is not null && view.UserId is not null && view.UserId != command.UserId)
            return RecordBlogPostViewError.VisitorClaimedByAnotherUser;

        if (view is null)
        {
            view = new BlogPostVisitorView
            {
                Id = id,
                Slug = command.Slug,
                VisitorId = command.VisitorId,
                UserId = command.UserId,
                ViewCount = 1,
                FirstViewedAtUtc = command.NowUtc,
                LastViewedAtUtc = command.NowUtc,
            };
        }
        else
        {
            // Stamp the account the first time a signed-in visit lands, so past anonymous views attribute.
            if (command.UserId is { } signedInId && view.UserId is null)
                view.UserId = signedInId;
            if (view.ShouldCountNewView(command.NowUtc, ViewWindow))
            {
                view.ViewCount++;
                view.LastViewedAtUtc = command.NowUtc;
            }
        }

        session.Store(view);
        await session.SaveChangesAsync(ct);

        var readerTotal = command.UserId is { } userId
            ? await session.Query<BlogPostVisitorView>().Where(v => v.Slug == command.Slug && v.UserId == userId).SumAsync(v => v.ViewCount, ct)
            : view.ViewCount;
        var postTotal = await session.Query<BlogPostVisitorView>().Where(v => v.Slug == command.Slug).SumAsync(v => v.ViewCount, ct);
        var uniqueVisitors = await session.CountDistinctViewersAsync(command.Slug, ct);
        // The conflict guard above ensures this row is the reader's own, so it's in readerTotal.
        return new RecordBlogPostViewResult(readerTotal - 1, postTotal, uniqueVisitors);
    }
}
