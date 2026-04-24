using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record EditJobOfferCommand(
    Guid Id,
    CurrentUser User,
    NonEmptyString? CompanyName,
    NonEmptyString? ContactName,
    NonEmptyString? ContactEmail,
    NonEmptyString? JobTitle,
    NonEmptyString? Description,
    string? SalaryRange,
    string? Location,
    bool? IsRemote,
    string? AdditionalNotes,
    DateTimeOffset Timestamp);

public class EditJobOfferHandler(IDocumentSession session)
{
    public async Task<Result<JobOffer, EditJobOfferError>> HandleAsync(
        EditJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return EditJobOfferError.NotFound;

        var result = offer.Edit(
            user: command.User,
            companyName: command.CompanyName,
            contactName: command.ContactName,
            contactEmail: command.ContactEmail,
            jobTitle: command.JobTitle,
            description: command.Description,
            salaryRange: command.SalaryRange,
            location: command.Location,
            isRemote: command.IsRemote,
            additionalNotes: command.AdditionalNotes,
            timestamp: command.Timestamp);

        if (result.Error is { } error)
            return error;

        var evt = result.Success!;
        stream.AppendOne(evt);
        offer.Apply(evt);
        await session.SaveChangesAsync(ct);
        return offer;
    }
}
