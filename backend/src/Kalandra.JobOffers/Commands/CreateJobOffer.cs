using System.Net.Mail;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using StrongTypes;

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

public class CreateJobOfferHandler(IDocumentSession session, IStorageService storageService)
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
    /// Stores the submitted offer; the owner notification is delivered separately by the
    /// job-offer subscription reacting to the appended event.
    /// </summary>
    public async Task<Result<Guid, CreateJobOfferError>> CreateAndSave(
        CreateJobOfferCommand command, CancellationToken ct)
    {
        // Validate attachments
        if (command.Files.Count > MaxAttachments)
            return CreateJobOfferError.TooManyAttachments;

        if (command.Files.Sum(f => f.FileSize) > MaxTotalBytes)
            return CreateJobOfferError.TotalSizeTooLarge;

        if (command.Files.Any(f => !AllowedContentTypes.Contains(f.ContentType)))
            return CreateJobOfferError.DisallowedContentType;

        // A resend of the same id is silent retry protection — the same user re-submitting the form.
        if (await session.LoadAsync<JobOffer>(command.Id, ct) is { } existing)
            return existing.UserId == command.User.Id ? command.Id : CreateJobOfferError.IdAlreadyUsed;

        // Upload attachments
        var uploadedAttachments = new List<AttachmentInfo>();

        if (command.Files.Count > 0)
        {
            var folderPrefix = $"{command.User.Id}/{command.Id}/";
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

        session.Events.StartStream<JobOffer>(command.Id, submitted);
        await session.SaveChangesAsync(ct);
        return command.Id;
    }
}
