using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Temporalio.Workflows;

namespace Kalandra.Blog.Workflows;

/// <summary>Slug travels as a raw string — BlogPostSlug has no public constructor for the payload converter.</summary>
public record BlogCommentWorkflowInput(string Slug, BlogCommentPosted Comment);

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

        if (storeOutcome!.Posted == null)
            return; // rejected by domain validation — nothing to notify

        await Workflow.ExecuteActivityAsync(
            (BlogCommentActivities activities) => activities.SendCommentNotificationsAsync(input),
            Options);
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
