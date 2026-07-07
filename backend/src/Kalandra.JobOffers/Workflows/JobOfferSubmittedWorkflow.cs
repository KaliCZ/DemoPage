using Kalandra.JobOffers.Events;
using Temporalio.Workflows;

namespace Kalandra.JobOffers.Workflows;

public record JobOfferSubmittedWorkflowInput(Guid JobOfferId, JobOfferSubmitted Submitted);

/// <summary>
/// One durable flow per submitted job offer: the update handler stores it (the API
/// returns as soon as that lands) and the run method then delivers the email
/// notifications — an offer is never stored without its notifications, nor
/// notified without being stored.
/// </summary>
[Workflow]
public class JobOfferSubmittedWorkflow
{
    public static string IdFor(Guid jobOfferId) => $"job-offer-submitted-{jobOfferId}";

    private static readonly ActivityOptions Options = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
    };

    private bool stored;

    [WorkflowRun]
    public async Task RunAsync(JobOfferSubmittedWorkflowInput input)
    {
        await Workflow.WaitConditionAsync(() => stored);

        var recipients = await Workflow.ExecuteActivityAsync(
            (JobOfferActivities activities) => activities.PlanSubmittedNotifications(input),
            Options);

        // One activity per email so a failed send retries on its own instead of re-delivering the rest.
        var sends = recipients
            .Select(recipient => Workflow.ExecuteActivityAsync(
                (JobOfferActivities activities) => activities.SendSubmittedNotificationAsync(recipient, input),
                Options))
            .ToList();
        await Task.WhenAll(sends);
    }

    [WorkflowUpdate]
    public async Task StoreJobOfferAsync(JobOfferSubmittedWorkflowInput input)
    {
        await Workflow.ExecuteActivityAsync(
            (JobOfferActivities activities) => activities.StoreJobOfferAsync(input),
            Options);
        stored = true;
    }
}
