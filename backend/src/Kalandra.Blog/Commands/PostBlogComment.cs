using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Workflows;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Kalandra.Blog.Commands;

public record PostBlogCommentCommand(BlogPost Post, BlogCommentPosted Comment);

public class PostBlogCommentHandler(ITemporalClient temporalClient)
{
    /// <summary>
    /// Drives the durable comment flow: store + notify share one workflow, and the
    /// update returns once the comment is stored — notifications continue async.
    /// </summary>
    public async Task<Result<BlogCommentPosted, PostBlogCommentError>> Post(
        PostBlogCommentCommand command, CancellationToken ct)
    {
        var input = new BlogCommentWorkflowInput(
            command.Post.Slug, command.Post.CommentsStreamId, command.Comment);

        var startOperation = WithStartWorkflowOperation.Create(
            (BlogCommentWorkflow workflow) => workflow.RunAsync(input),
            new(id: BlogCommentWorkflow.IdFor(command.Comment.CommentId), taskQueue: BlogTaskQueue.Name)
            {
                // A client retry of the same request reattaches instead of failing.
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });

        // Cancellation frees the request if the client disconnects; the workflow keeps
        // running — durability is the point.
        var outcome = await temporalClient.ExecuteUpdateWithStartWorkflowAsync(
            (BlogCommentWorkflow workflow) => workflow.StoreCommentAsync(input),
            new(startOperation) { Rpc = new() { CancellationToken = ct } });

        if (outcome.Error is { } error)
            return error;

        return outcome.Posted!;
    }
}
