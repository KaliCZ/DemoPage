using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.TestErrors;

[ApiController]
[Route("api/test-errors")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Admin)]
public class TestErrorsController(
    ICurrentUserAccessor currentUser,
    ListJobOffersHandler listHandler) : ControllerBase
{
    /// <summary>Throws an exception immediately. Used to verify error tracking.</summary>
    [HttpPost("throw")]
    public IActionResult Throw()
    {
        throw new InvalidOperationException("Test error triggered by admin.");
    }

    /// <summary>Fetches one page of job offers, then throws. Exercises a real DB query path before crashing.</summary>
    [HttpGet("crash-after-data")]
    public async Task<IActionResult> CrashAfterData(CancellationToken ct)
    {
        var query = new ListJobOffersQuery(currentUser.RequiredUser, ShowAll: true, Statuses: null, Page: 1, PageSize: 1);
        _ = await listHandler.HandleAsync(query, ct);

        throw new InvalidOperationException("Test error after successful data fetch, triggered by admin.");
    }
}
