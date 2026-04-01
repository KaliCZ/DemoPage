using FluentValidation;

namespace Kalandra.Api.Features.JobOffers.Edit;

public class EditJobOfferValidator : AbstractValidator<EditJobOfferRequest>
{
    public EditJobOfferValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.SalaryRange).MaximumLength(100);
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.AdditionalNotes).MaximumLength(2000);
    }
}
