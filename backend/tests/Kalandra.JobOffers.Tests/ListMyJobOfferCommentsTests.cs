using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Queries;

namespace Kalandra.JobOffers.Tests;

public class ListMyJobOfferCommentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid MyId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly JobOffer Offer = new() { Id = Guid.NewGuid() };

    private static JobOfferCommentAdded NewComment(Guid userId, string content, int minutesAfterNow) => new(
        JobOfferId: Offer.Id,
        CommentId: Guid.NewGuid(),
        UserId: userId,
        UserEmail: Email.Create($"{userId}@test.com"),
        UserName: "Someone".ToNonEmpty(),
        Content: content.ToNonEmpty(),
        Timestamp: Now.AddMinutes(minutesAfterNow));

    [Fact]
    public void Collect_AttachesOtherAuthorsCommentsToLatestPrecedingMine()
    {
        var mineFirst = NewComment(MyId, "First question", 0);
        var ownerReply = NewComment(OwnerId, "Answer to first", 1);
        var mineSecond = NewComment(MyId, "Second question", 2);
        var ownerReplyTwo = NewComment(OwnerId, "Answer to second", 3);

        var result = ListMyJobOfferCommentsHandler
            .Collect(Offer, [mineFirst, ownerReply, mineSecond, ownerReplyTwo], MyId)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(mineFirst.CommentId, result[0].Comment.CommentId);
        Assert.Equal(ownerReply.CommentId, Assert.Single(result[0].Replies).CommentId);
        Assert.Equal(mineSecond.CommentId, result[1].Comment.CommentId);
        Assert.Equal(ownerReplyTwo.CommentId, Assert.Single(result[1].Replies).CommentId);
    }

    [Fact]
    public void Collect_DropsOtherAuthorsCommentsBeforeMyFirst()
    {
        var ownerOpens = NewComment(OwnerId, "Owner opens the thread", 0);
        var mine = NewComment(MyId, "My comment", 1);

        var result = ListMyJobOfferCommentsHandler.Collect(Offer, [ownerOpens, mine], MyId).ToList();

        var entry = Assert.Single(result);
        Assert.Equal(mine.CommentId, entry.Comment.CommentId);
        Assert.Empty(entry.Replies);
    }

    [Fact]
    public void Collect_GroupsConsecutiveRepliesUnderTheSameComment()
    {
        var mine = NewComment(MyId, "Question", 0);
        var replyOne = NewComment(OwnerId, "Part one", 1);
        var replyTwo = NewComment(OwnerId, "Part two", 2);

        var result = ListMyJobOfferCommentsHandler.Collect(Offer, [mine, replyOne, replyTwo], MyId).ToList();

        var entry = Assert.Single(result);
        Assert.Equal([replyOne.CommentId, replyTwo.CommentId], entry.Replies.Select(r => r.CommentId));
    }

    [Fact]
    public void Collect_SortsByTimestampBeforePairing()
    {
        var mine = NewComment(MyId, "Question", 0);
        var reply = NewComment(OwnerId, "Answer", 1);

        // Same thread handed over out of order — pairing must go by time, not input order.
        var result = ListMyJobOfferCommentsHandler.Collect(Offer, [reply, mine], MyId).ToList();

        var entry = Assert.Single(result);
        Assert.Equal(reply.CommentId, Assert.Single(entry.Replies).CommentId);
    }

    [Fact]
    public void Collect_ReturnsNothingForUserWithoutComments()
    {
        var ownerOnly = NewComment(OwnerId, "Owner monologue", 0);

        Assert.Empty(ListMyJobOfferCommentsHandler.Collect(Offer, [ownerOnly], MyId));
    }
}
