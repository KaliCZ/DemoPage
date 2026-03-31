using FluentValidation;

namespace Kalandra.Api.Features.JobOffers.Create;

public class CreateJobOfferValidator : AbstractValidator<CreateJobOfferRequest>
{
    private const int MaxAttachments = 5;
    private const long MaxTotalBytes = 15 * 1024 * 1024; // 15 MB

    public CreateJobOfferValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.SalaryRange).MaximumLength(100);
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.AdditionalNotes).MaximumLength(2000);

        When(x => x.Attachments != null && x.Attachments.Count > 0, () =>
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage("An offer ID is required when attachments are included.");

            RuleFor(x => x.Attachments!.Count)
                .LessThanOrEqualTo(MaxAttachments)
                .WithMessage($"Maximum {MaxAttachments} attachments allowed.");

            RuleFor(x => x.Attachments!.Sum(a => a.FileSize))
                .LessThanOrEqualTo(MaxTotalBytes)
                .WithMessage("Total attachment size must not exceed 15 MB.");

            RuleForEach(x => x.Attachments).ChildRules(a =>
            {
                a.RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
                a.RuleFor(x => x.StoragePath).NotEmpty().MaximumLength(500);
                a.RuleFor(x => x.FileSize).GreaterThan(0);
                a.RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
            });
        });
    }
}
