using System.Net.Mail;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Workflows;
using StrongTypes;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Kalandra.JobOffers.Commands;

public enum CreateJobOfferError { TooManyAttachments, TotalSizeTooLarge, DisallowedContentType, IdAlreadyUsed }

public record CreateJobOfferFile(
    NonEmptyString FileName,
    long FileSize,
    NonEmptyString ContentType,
    Stream Content);

public record CreateJobOfferCommand(
    Guid Id,
    CurrentUser User,
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    MailAddress ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    IReadOnlyList<CreateJobOfferFile> Files,
    DateTimeOffset Timestamp);

public class CreateJobOfferHandler(ITemporalClient temporalClient, IStorageService storageService)
{
    private const int MaxAttachments = 5;
    private const long MaxTotalBytes = 15 * 1024 * 1024; // 15 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "image/png",
        "image/jpeg",
        "image/webp",
    };

    /// <summary>
    /// Drives the durable submission flow: store + notify share one workflow, and the
    /// update returns once the offer is stored — notifications continue async.
    /// </summary>
    public async Task<Result<Guid, CreateJobOfferError>> Create(
        CreateJobOfferCommand command, CancellationToken ct)
    {
        // Validate attachments
        if (command.Files.Count > MaxAttachments)
            return CreateJobOfferError.TooManyAttachments;

        if (command.Files.Sum(f => f.FileSize) > MaxTotalBytes)
            return CreateJobOfferError.TotalSizeTooLarge;

        if (command.Files.Any(f => !AllowedContentTypes.Contains(f.ContentType)))
            return CreateJobOfferError.DisallowedContentType;

        // Upload attachments
        var streamId = command.Id;
        var uploadedAttachments = new List<AttachmentInfo>();

        if (command.Files.Count > 0)
        {
            var folderPrefix = $"{command.User.Id}/{streamId}/";
            var items = command.Files
                .Select(f => new FileUploadItem(f.FileName, f.FileSize, f.ContentType, f.Content))
                .ToList();

            var uploaded = await storageService.UploadAsync(folderPrefix, items, ct);
            uploadedAttachments = uploaded
                .Select(f => new AttachmentInfo(
                    FileName: f.FileName,
                    StoragePath: f.StoragePath,
                    FileSize: f.FileSize,
                    ContentType: f.ContentType))
                .ToList();
        }

        // Create event
        var submitted = new JobOfferSubmitted(
            UserId: command.User.Id,
            UserEmail: command.User.Email,
            CompanyName: command.CompanyName,
            ContactName: command.ContactName,
            ContactEmail: command.ContactEmail,
            JobTitle: command.JobTitle,
            Description: command.Description,
            SalaryRange: command.SalaryRange,
            Location: command.Location,
            IsRemote: command.IsRemote,
            AdditionalNotes: command.AdditionalNotes,
            Attachments: uploadedAttachments,
            Timestamp: command.Timestamp);

        var input = new JobOfferSubmittedWorkflowInput(streamId, submitted);

        var startOperation = WithStartWorkflowOperation.Create(
            (JobOfferSubmittedWorkflow workflow) => workflow.RunAsync(input),
            new(id: JobOfferSubmittedWorkflow.IdFor(streamId), taskQueue: JobOffersTaskQueue.Name)
            {
                // A client retry of the same request reattaches instead of failing.
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });

        // Cancellation frees the request if the client disconnects; the workflow keeps
        // running — durability is the point.
        var outcome = await temporalClient.ExecuteUpdateWithStartWorkflowAsync(
            (JobOfferSubmittedWorkflow workflow) => workflow.StoreJobOfferAsync(input),
            new(startOperation) { Rpc = new() { CancellationToken = ct } });

        if (outcome.Error is { } error)
            return error;

        return streamId;
    }
}
