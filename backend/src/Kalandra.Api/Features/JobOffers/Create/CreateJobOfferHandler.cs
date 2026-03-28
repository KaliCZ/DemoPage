using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Infrastructure.Database;

namespace Kalandra.Api.Features.JobOffers.Create;

public class CreateJobOfferHandler
{
    private readonly AppDbContext _db;

    public CreateJobOfferHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CreateJobOfferResponse> HandleAsync(
        CreateJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var jobOffer = new JobOffer
        {
            UserId = userId,
            UserEmail = userEmail,
            CompanyName = request.CompanyName,
            ContactName = request.ContactName,
            ContactEmail = request.ContactEmail,
            JobTitle = request.JobTitle,
            Description = request.Description,
            SalaryRange = request.SalaryRange,
            Location = request.Location,
            IsRemote = request.IsRemote,
            AdditionalNotes = request.AdditionalNotes,
            Status = JobOfferStatus.Submitted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.JobOffers.Add(jobOffer);
        await _db.SaveChangesAsync(ct);

        return new CreateJobOfferResponse(jobOffer.Id, jobOffer.CreatedAt);
    }
}
