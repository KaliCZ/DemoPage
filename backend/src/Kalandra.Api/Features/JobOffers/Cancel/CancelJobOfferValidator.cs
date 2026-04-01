using FluentValidation;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public class CancelJobOfferValidator : AbstractValidator<CancelJobOfferRequest>
{
    public CancelJobOfferValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(2000);
    }
}
