using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Workflows;
using StrongTypes;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Kalandra.JobOffers.Commands;

public record AddCommentCommand(
    Guid JobOfferId,
    CurrentUser User,
    NonEmptyString Content,
    DateTimeOffset Timestamp);

public class AddCommentHandler(ITemporalClient temporalClient)
{
    /// <summary>
    /// Drives the durable comment flow: store + notify share one workflow, and the
    /// update returns once the comment is stored — notifications continue async.
    /// </summary>
    public async Task<Result<JobOfferCommentAdded, AddCommentError>> Add(
        AddCommentCommand command, CancellationToken ct)
    {
        var comment = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: command.User.Id,
            UserEmail: command.User.Email,
            UserName: command.User.FullName,
            Content: command.Content,
            Timestamp: command.Timestamp);

        var input = new JobOfferCommentWorkflowInput(command.JobOfferId, comment, command.User.IsAdmin);

        var startOperation = WithStartWorkflowOperation.Create(
            (JobOfferCommentWorkflow workflow) => workflow.RunAsync(input),
            new(id: JobOfferCommentWorkflow.IdFor(comment.CommentId), taskQueue: JobOffersTaskQueue.Name)
            {
                // An internal RPC retry reattaches instead of failing.
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });

        // Cancellation frees the request if the client disconnects; the workflow keeps
        // running — durability is the point.
        var outcome = await temporalClient.ExecuteUpdateWithStartWorkflowAsync(
            (JobOfferCommentWorkflow workflow) => workflow.StoreCommentAsync(input),
            new(startOperation) { Rpc = new() { CancellationToken = ct } });

        if (outcome.Error is { } error)
            return error;

        return outcome.Stored!.Comment;
    }
}
