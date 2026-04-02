using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record EditJobOfferCommand(
    Guid Id,
    NonEmptyString UserId,
    NonEmptyString UserEmail,
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    NonEmptyString ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    DateTimeOffset Timestamp);

/// <summary>
/// Validates and appends an edit event. Does not save — the caller commits the session.
/// </summary>
public class EditJobOfferHandler(IDocumentSession session)
{
    public async Task<Try<Unit, EditJobOfferError>> HandleAsync(
        EditJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<Unit, EditJobOfferError>(EditJobOfferError.NotFound);

        var result = offer.Edit(
            userId: command.UserId.Value,
            userEmail: command.UserEmail.Value,
            companyName: command.CompanyName.Value,
            contactName: command.ContactName.Value,
            contactEmail: command.ContactEmail.Value,
            jobTitle: command.JobTitle.Value,
            description: command.Description.Value,
            salaryRange: command.SalaryRange,
            location: command.Location,
            isRemote: command.IsRemote,
            additionalNotes: command.AdditionalNotes,
            timestamp: command.Timestamp);

        return result.Match(
            evt =>
            {
                stream.AppendOne(evt);
                return Try.Success<Unit, EditJobOfferError>(Unit.Value);
            },
            err => Try.Error<Unit, EditJobOfferError>(err));
    }
}
