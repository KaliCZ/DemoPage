using FluentValidation;

namespace Kalandra.Api.Features.JobOffers.Comments;

public class AddCommentValidator : AbstractValidator<AddCommentRequest>
{
    public AddCommentValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
    }
}
