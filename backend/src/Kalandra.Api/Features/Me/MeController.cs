using Kalandra.Api.Features.Me.Contracts;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Blog.Queries;
using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Me;

[ApiController]
[Route("api/me")]
[Produces("application/json")]
[Authorize]
public class MeController(
    ICurrentUserAccessor currentUser,
    ListMyBlogCommentsHandler myBlogCommentsHandler,
    ListMyJobOfferCommentsHandler myJobOfferCommentsHandler) : ControllerBase
{
    private CurrentUser AppUser => currentUser.RequiredUser;

    /// <summary>The caller's comments across blog posts and job offers, with the replies they received.</summary>
    [HttpGet("comments")]
    [ProducesResponseType<GetMyCommentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetMyCommentsResponse>> GetComments(CancellationToken ct)
    {
        var blogComments = await myBlogCommentsHandler.List(new ListMyBlogCommentsQuery(AppUser), ct);
        var jobOfferComments = await myJobOfferCommentsHandler.List(new ListMyJobOfferCommentsQuery(AppUser), ct);

        return GetMyCommentsResponse.Serialize(blogComments, jobOfferComments);
    }
}
