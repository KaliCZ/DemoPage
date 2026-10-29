using Kalandra.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.TestErrors;

[ApiController]
[Route("api/test-errors")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Admin)]
public class TestErrorsController(ILogger<TestErrorsController> logger) : ControllerBase
{
    /// <summary>Lets an admin verify error tracking end to end.</summary>
    [HttpPost("throw")]
    public IActionResult Throw()
    {
        // Info-level log first so we can verify structured-log delivery alongside the exception event.
        logger.LogInformation("Test endpoint /api/test-errors/throw invoked — about to throw.");
        throw new InvalidOperationException("Test error triggered by admin.");
    }
}
