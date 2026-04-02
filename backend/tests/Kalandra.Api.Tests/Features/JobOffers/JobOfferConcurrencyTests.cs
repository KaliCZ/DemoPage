using Kalandra.JobOffers;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
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

    [Fact]
    public async Task CommentAppend_DoesNotConflict_WithFetchForWriting()
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

        // Writer fetches stream for an optimistic-concurrency edit
        await using var writerSession = store.LightweightSession();
        var writer = await writerSession.Events.FetchForWriting<JobOffer>(jobOfferId, Ct);

        // Meanwhile, a comment is appended to the separate comment stream
        await using (var commentSession = store.LightweightSession())
        {
            var commentStreamId = CommentStreamId.For(jobOfferId);
            commentSession.Events.Append(
                commentStreamId,
                new JobOfferCommentAdded(
                    CommentId: Guid.NewGuid(),
                    UserId: "owner-user",
                    UserEmail: "owner@test.com",
                    UserName: "Owner",
                    Content: "Any updates?",
                    Timestamp: DateTimeOffset.UtcNow));
            await commentSession.SaveChangesAsync(Ct);
        }

        // The writer's save should succeed — comment didn't touch the job offer stream
        writer.AppendOne(new JobOfferStatusChanged(
            ChangedByUserId: "admin-1",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: DateTimeOffset.UtcNow));

        await writerSession.SaveChangesAsync(Ct);

        // Verify both the status change and comment are visible
        await using var verifySession = store.LightweightSession();
        var offer = await verifySession.LoadAsync<JobOffer>(jobOfferId, Ct);
        Assert.Equal(JobOfferStatus.InReview, offer!.Status);

        var commentStreamEvents = await verifySession.Events.FetchStreamAsync(
            CommentStreamId.For(jobOfferId), token: Ct);
        Assert.Single(commentStreamEvents);
        Assert.IsType<JobOfferCommentAdded>(commentStreamEvents[0].Data);
    }
}
