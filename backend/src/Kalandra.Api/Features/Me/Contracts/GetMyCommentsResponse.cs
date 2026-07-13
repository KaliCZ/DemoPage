using Kalandra.Api.Features.Blog.Contracts;
using Kalandra.Api.Features.JobOffers.Contracts;
using Kalandra.Blog.Queries;
using Kalandra.JobOffers.Queries;
using StrongTypes;

namespace Kalandra.Api.Features.Me.Contracts;

// Reuses the Blog and JobOffers comment responses so a comment looks the same here
// as on the per-post and per-offer endpoints.
public record MyBlogCommentResponse(
    string Slug,
    BlogCommentResponse Comment,
    IReadOnlyList<BlogCommentResponse> Replies)
{
    public static MyBlogCommentResponse Serialize(MyBlogComment entry) => new(
        Slug: entry.Post.Slug,
        Comment: BlogCommentResponse.Serialize(entry.Comment),
        Replies: [.. entry.Replies.Select(BlogCommentResponse.Serialize)]);
}

public record MyJobOfferCommentResponse(
    Guid JobOfferId,
    NonEmptyString JobTitle,
    NonEmptyString CompanyName,
    CommentResponse Comment,
    IReadOnlyList<CommentResponse> Replies)
{
    public static MyJobOfferCommentResponse Serialize(MyJobOfferComment entry) => new(
        JobOfferId: entry.Offer.Id,
        JobTitle: entry.Offer.JobTitle,
        CompanyName: entry.Offer.CompanyName,
        Comment: CommentResponse.Serialize(entry.Comment),
        Replies: [.. entry.Replies.Select(CommentResponse.Serialize)]);
}

public record GetMyCommentsResponse(
    IReadOnlyList<MyBlogCommentResponse> BlogComments,
    IReadOnlyList<MyJobOfferCommentResponse> JobOfferComments)
{
    public static GetMyCommentsResponse Serialize(
        IReadOnlyList<MyBlogComment> blogComments,
        IReadOnlyList<MyJobOfferComment> jobOfferComments) => new(
        BlogComments: [.. blogComments.Select(MyBlogCommentResponse.Serialize)],
        JobOfferComments: [.. jobOfferComments.Select(MyJobOfferCommentResponse.Serialize)]);
}
