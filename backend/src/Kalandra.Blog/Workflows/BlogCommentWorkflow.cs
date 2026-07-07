using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Temporalio.Workflows;

namespace Kalandra.Blog.Workflows;

/// <summary>Carries the comment stream id it stores to and the slug the notification links point at.</summary>
public record BlogCommentWorkflowInput(string Slug, Guid CommentsStreamId, BlogCommentPosted Comment);

public record StoreBlogCommentOutcome(BlogCommentPosted? Posted, PostBlogCommentError? Error);

/// <summary>
/// One durable flow per posted comment: the update handler stores it (the API
/// returns as soon as that lands) and the run method then delivers the email
/// notifications — a comment is never stored without its notifications, nor
/// notified without being stored.
/// </summary>
[Workflow]
public class BlogCommentWorkflow
{
    public static string IdFor(Guid commentId) => $"blog-comment-{commentId}";

    private static readonly ActivityOptions Options = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
    };

    private StoreBlogCommentOutcome? storeOutcome;

    [WorkflowRun]
    public async Task RunAsync(BlogCommentWorkflowInput input)
    {
        await Workflow.WaitConditionAsync(() => storeOutcome != null);

        if (storeOutcome!.Posted != null)
        {
            var notifications = await Workflow.ExecuteActivityAsync(
                (BlogCommentActivities activities) => activities.PlanCommentNotificationsAsync(input),
                Options);

            // One activity per email so a failed send retries on its own instead of re-delivering the rest.
            var sends = notifications
                .Select(notification => Workflow.ExecuteActivityAsync(
                    (BlogCommentActivities activities) => activities.SendCommentNotificationAsync(notification, input),
                    Options))
                .ToList();
            await Task.WhenAll(sends);
        }

        // Closing while a late client-retry update is still storing would abort it with an RPC error.
        await Workflow.WaitConditionAsync(() => Workflow.AllHandlersFinished);
    }

    [WorkflowUpdate]
    public async Task<StoreBlogCommentOutcome> StoreCommentAsync(BlogCommentWorkflowInput input)
    {
        storeOutcome = await Workflow.ExecuteActivityAsync(
            (BlogCommentActivities activities) => activities.StoreCommentAsync(input),
            Options);
        return storeOutcome;
    }
}
