using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Temporalio.Workflows;

namespace Kalandra.JobOffers.Workflows;

/// <summary>Carries the admin flag because the store activity re-checks authorization outside the HTTP request.</summary>
public record JobOfferCommentWorkflowInput(Guid JobOfferId, JobOfferCommentAdded Comment, bool CommenterIsAdmin);

public record StoreJobOfferCommentOutcome(StoredJobOfferComment? Stored, AddCommentError? Error);

/// <summary>
/// One durable flow per added comment: the update handler stores it (the API
/// returns as soon as that lands) and the run method then delivers the email
/// notifications — a comment is never stored without its notifications, nor
/// notified without being stored.
/// </summary>
[Workflow]
public class JobOfferCommentWorkflow
{
    public static string IdFor(Guid commentId) => $"job-offer-comment-{commentId}";

    private static readonly ActivityOptions Options = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
    };

    private StoreJobOfferCommentOutcome? storeOutcome;

    [WorkflowRun]
    public async Task RunAsync(JobOfferCommentWorkflowInput input)
    {
        await Workflow.WaitConditionAsync(() => storeOutcome != null);

        if (storeOutcome!.Stored is not { } stored)
            return; // rejected by domain validation — nothing to notify

        var notifications = await Workflow.ExecuteActivityAsync(
            (JobOfferActivities activities) => activities.PlanCommentNotifications(stored),
            Options);

        // One activity per email so a failed send retries on its own instead of re-delivering the rest.
        var sends = notifications
            .Select(notification => Workflow.ExecuteActivityAsync(
                (JobOfferActivities activities) => activities.SendCommentNotificationAsync(notification, stored),
                Options))
            .ToList();
        await Task.WhenAll(sends);
    }

    [WorkflowUpdate]
    public async Task<StoreJobOfferCommentOutcome> StoreCommentAsync(JobOfferCommentWorkflowInput input)
    {
        storeOutcome = await Workflow.ExecuteActivityAsync(
            (JobOfferActivities activities) => activities.StoreCommentAsync(input),
            Options);
        return storeOutcome;
    }
}
