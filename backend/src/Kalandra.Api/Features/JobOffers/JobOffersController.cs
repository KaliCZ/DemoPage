using FluentValidation;
using Kalandra.Api.Features.JobOffers.Cancel;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Features.JobOffers.UpdateStatus;
using Kalandra.Api.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
public class JobOffersController : ControllerBase
{
    private readonly IDocumentSession _session;

    public JobOffersController(IDocumentSession session)
    {
        _session = session;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        [FromBody] CreateJobOfferRequest request,
        [FromServices] IValidator<CreateJobOfferRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";

        var handler = new CreateJobOfferHandler(_session);
        var result = await handler.HandleAsync(request, userId, email, ct);

        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var handler = new ListJobOffersHandler(_session);
        var result = await handler.HandleAsync(userId, ct);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var handler = new ListJobOffersHandler(_session);
        var result = await handler.HandleAsync(null, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new GetJobOfferDetailHandler(_session);
        var result = await handler.HandleAsync(id, userId, isAdmin, ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [Authorize]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new JobOfferHistoryHandler(_session);
        var result = await handler.HandleAsync(id, userId, isAdmin, ct);

        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelJobOfferRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var email = User.GetEmail() ?? "";

        var handler = new CancelJobOfferHandler(_session);
        var (success, error) = await handler.HandleAsync(id, request, userId, email, ct);

        if (!success)
            return error == "Not found" ? NotFound() :
                   error == "Not authorized" ? Forbid() :
                   BadRequest(new { error });

        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        var adminUserId = User.GetUserId()!;
        var adminEmail = User.GetEmail() ?? "";

        var handler = new UpdateJobOfferStatusHandler(_session);
        var success = await handler.HandleAsync(id, request, adminUserId, adminEmail, ct);

        return success ? NoContent() : NotFound();
    }
}
