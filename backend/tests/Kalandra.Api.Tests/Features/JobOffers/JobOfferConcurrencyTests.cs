using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Kalandra.Api.Tests.Helpers;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Api.Tests.Features.JobOffers;

public class JobOfferConcurrencyTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly IDocumentStore store = factory.Services.GetRequiredService<IDocumentStore>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FetchForWriting_RejectsStaleSecondWriter()
    {
        var jobOfferId = Guid.NewGuid();

        await using (var seedSession = store.LightweightSession())
        {
            seedSession.Events.StartStream<JobOffer>(
                id: jobOfferId,
                new JobOfferSubmitted(
                    UserId: "owner-user",
                    UserEmail: "owner@test.com",
                    CompanyName: "Acme Corp",
                    ContactName: "John Doe",
                    ContactEmail: "john@acme.com",
                    JobTitle: "Senior Developer",
                    Description: "Original description",
                    SalaryRange: null,
                    Location: "Prague",
                    IsRemote: true,
                    AdditionalNotes: null,
                    Attachments: [],
                    Timestamp: DateTimeOffset.UtcNow));

            await seedSession.SaveChangesAsync(Ct);
        }

        await using var firstSession = store.LightweightSession();
        await using var secondSession = store.LightweightSession();

        var firstWriter = await firstSession.Events.FetchForWriting<JobOffer>(jobOfferId, Ct);
        var secondWriter = await secondSession.Events.FetchForWriting<JobOffer>(jobOfferId, Ct);

        firstWriter.AppendOne(new JobOfferStatusChanged(
            ChangedByUserId: "admin-1",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: DateTimeOffset.UtcNow));

        await firstSession.SaveChangesAsync(Ct);

        secondWriter.AppendOne(new JobOfferEdited(
            EditedByUserId: "owner-user",
            EditedByEmail: "owner@test.com",
            CompanyName: "Acme Corp",
            ContactName: "John Doe",
            ContactEmail: "john@acme.com",
            JobTitle: "Principal Engineer",
            Description: "Edited with stale version",
            SalaryRange: null,
            Location: "Prague",
            IsRemote: true,
            AdditionalNotes: null,
            Timestamp: DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => secondSession.SaveChangesAsync(Ct));
        Assert.True(exception is ConcurrencyException or EventStreamUnexpectedMaxEventIdException);
    }
}
