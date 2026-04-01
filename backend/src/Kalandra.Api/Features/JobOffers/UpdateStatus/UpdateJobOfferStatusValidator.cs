using FluentValidation;
using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusValidator : AbstractValidator<UpdateJobOfferStatusRequest>
{
    public UpdateJobOfferStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.AdminNotes).MaximumLength(2000);
    }
}
