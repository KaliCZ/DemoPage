using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Commands;

public record RecordBlogPostViewCommand(string Slug, Guid VisitorId, Guid? UserId, DateTimeOffset NowUtc);

/// <summary>
/// PreviousViewCount is the reader's own visits before this one; TotalViews is the post's
/// public total (page views); UniqueVisitors is how many distinct people it reached.
/// </summary>
public record RecordBlogPostViewResult(int PreviousViewCount, int TotalViews, int UniqueVisitors);

public class RecordBlogPostViewHandler(IDocumentSession session)
{
    // A refresh inside this window is the same visit; a return after it is a new view.
    public static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Recording a view has no failure mode, so this returns counts directly instead of a
    /// Result: the reader's own count before this visit (signed-in readers get their
    /// cross-device total, anonymous readers just this browser's) plus the post's total.
    /// </summary>
    public async Task<RecordBlogPostViewResult> RecordAndSave(RecordBlogPostViewCommand command, CancellationToken ct)
    {
        var id = BlogPostVisitorView.IdFor(command.Slug, command.VisitorId);
        var view = await session.LoadAsync<BlogPostVisitorView>(id, ct);
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
        // One document per (post, visitor), so the row count is the number of distinct visitors.
        var uniqueVisitors = await session.Query<BlogPostVisitorView>().Where(v => v.Slug == command.Slug).CountAsync(ct);
        return new RecordBlogPostViewResult(readerTotal - 1, postTotal, uniqueVisitors);
    }
}
