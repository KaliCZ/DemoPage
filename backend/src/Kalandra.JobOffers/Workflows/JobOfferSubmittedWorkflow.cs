using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Events;
using Temporalio.Workflows;

namespace Kalandra.JobOffers.Workflows;

public record JobOfferSubmittedWorkflowInput(Guid JobOfferId, JobOfferSubmitted Submitted);

public record StoreJobOfferOutcome(CreateJobOfferError? Error);

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

    private StoreJobOfferOutcome? storeOutcome;

    [WorkflowRun]
    public async Task RunAsync(JobOfferSubmittedWorkflowInput input)
    {
        await Workflow.WaitConditionAsync(() => storeOutcome != null);

        if (storeOutcome!.Error == null)
        {
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

        // Temporal's prescribed completion guard (message-passing docs): returning while a
        // client retry's update is still storing would abort that update with an RPC error.
        await Workflow.WaitConditionAsync(() => Workflow.AllHandlersFinished);
    }

    [WorkflowUpdate]
    public async Task<StoreJobOfferOutcome> StoreJobOfferAsync(JobOfferSubmittedWorkflowInput input)
    {
        storeOutcome = await Workflow.ExecuteActivityAsync(
            (JobOfferActivities activities) => activities.StoreJobOfferAsync(input),
            Options);
        return storeOutcome;
    }
}
