using System.ComponentModel;
using Kalandra.Api.Features.JobOffers.Contracts;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Queries;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kalandra.Api.Features.Mcp;

/// <summary>
/// MCP tools for job offers. Each is a thin adapter over the same domain handlers the
/// controllers call, acting as the authenticated user — no separate write path.
/// </summary>
[McpServerToolType]
public sealed class JobOfferMcpTools(
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    CreateJobOfferHandler createHandler,
    GetJobOfferDetailHandler getDetailHandler,
    ListJobOffersHandler listHandler,
    ListCommentsHandler listCommentsHandler,
    AddCommentHandler addCommentHandler)
{
    [McpServerTool(Name = "submit_job_offer")]
    [Description("Submit a job offer to Pavel Kalandra — the same flow as the site's 'Hire me' form. " +
                 "Requires the user's kalandra.tech account. Returns the created offer, whose id can be used to follow up with comments.")]
    public async Task<GetJobOfferDetailResponse> SubmitJobOffer(
        [Description("Name of the hiring company.")] string companyName,
        [Description("Full name of the contact person.")] string contactName,
        [Description("Email address of the contact person.")] string contactEmail,
        [Description("Title of the offered position.")] string jobTitle,
        [Description("Description of the role, stack, and team.")] string description,
        [Description("Whether the position can be done fully remotely.")] bool isRemote,
        [Description("Offered salary range, e.g. '120–150k EUR/year'. Optional.")] string? salaryRange = null,
        [Description("Where the job is located, e.g. 'Prague'. Optional.")] string? location = null,
        [Description("Anything else worth mentioning. Optional.")] string? additionalNotes = null,
        CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);

        var command = new CreateJobOfferCommand(
            Id: Guid.NewGuid(),
            User: user,
            CompanyName: McpToolHelpers.Required(companyName, nameof(companyName)),
            ContactName: McpToolHelpers.Required(contactName, nameof(contactName)),
            ContactEmail: McpToolHelpers.ParseEmail(contactEmail),
            JobTitle: McpToolHelpers.Required(jobTitle, nameof(jobTitle)),
            Description: McpToolHelpers.Required(description, nameof(description)),
            SalaryRange: salaryRange,
            Location: location,
            IsRemote: isRemote,
            AdditionalNotes: additionalNotes,
            Files: [],
            Timestamp: timeProvider.GetUtcNow());

        var result = await createHandler.CreateAndSave(command, ct);
        if (result.Error is { } error)
            throw new McpException(error switch
            {
                CreateJobOfferError.TooManyAttachments => "Too many attachments.",
                CreateJobOfferError.TotalSizeTooLarge => "The attachments exceed the total size limit.",
                CreateJobOfferError.DisallowedContentType => "One of the attachments has a disallowed content type.",
                CreateJobOfferError.IdAlreadyUsed => "That job offer id is already in use.",
            });

        var offer = await getDetailHandler.Get(new GetJobOfferDetailQuery(result.Success!.Value, user), ct);
        return GetJobOfferDetailResponse.Serialize(offer!);
    }

    [McpServerTool(Name = "list_my_job_offers")]
    [Description("List the job offers the user has submitted to kalandra.tech, with their current status.")]
    public async Task<IReadOnlyList<GetJobOfferDetailResponse>> ListMyJobOffers(CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);
        var query = new ListJobOffersQuery(user, ShowAll: false, Statuses: null, Page: 1, PageSize: 50);
        var offers = await listHandler.List(query, ct);
        return offers.Select(GetJobOfferDetailResponse.Serialize).ToList();
    }

    [McpServerTool(Name = "get_job_offer_comments")]
    [Description("Read the comment thread on one of the user's job offers (visible to the offer's author and the site owner).")]
    public async Task<IReadOnlyList<CommentResponse>> GetJobOfferComments(
        [Description("Id of the job offer, from submit_job_offer or list_my_job_offers.")] Guid jobOfferId,
        CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);
        var comments = await listCommentsHandler.List(new ListCommentsQuery(jobOfferId, user), ct);
        if (comments is null)
            throw new McpException("No such job offer, or it doesn't belong to you.");
        return comments.Select(CommentResponse.Serialize).ToList();
    }

    [McpServerTool(Name = "add_job_offer_comment")]
    [Description("Add a comment to one of the user's job offers — the conversation channel with the site owner about that offer.")]
    public async Task<CommentResponse> AddJobOfferComment(
        [Description("Id of the job offer, from submit_job_offer or list_my_job_offers.")] Guid jobOfferId,
        [Description("The comment text.")] string content,
        CancellationToken ct = default)
    {
        var user = McpToolHelpers.RequireUser(currentUser);

        var command = new AddCommentCommand(
            JobOfferId: jobOfferId,
            CommentId: Guid.NewGuid(),
            User: user,
            Content: McpToolHelpers.Required(content, nameof(content)),
            Timestamp: timeProvider.GetUtcNow());

        var result = await addCommentHandler.AddAndSave(command, ct);
        if (result.Error is { } error)
            throw new McpException(error switch
            {
                AddCommentError.NotFound => "No such job offer.",
                AddCommentError.NotAuthorized => "That job offer doesn't belong to you.",
            });

        return CommentResponse.Serialize(result.Success!);
    }
}
