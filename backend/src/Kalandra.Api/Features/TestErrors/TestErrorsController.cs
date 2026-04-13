using Kalandra.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.TestErrors;

[ApiController]
[Route("api/test-errors")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Admin)]
public class TestErrorsController : ControllerBase
{
    /// <summary>Throws an exception immediately. Used to verify error tracking.</summary>
    [HttpPost("throw")]
    public IActionResult Throw()
    {
        throw new InvalidOperationException("Test error triggered by admin.");
    }
}
