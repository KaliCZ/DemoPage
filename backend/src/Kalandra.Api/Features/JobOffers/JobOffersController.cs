using FluentValidation;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Features.JobOffers.UpdateStatus;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.JobOffers;

[ApiController]
[Route("api/job-offers")]
public class JobOffersController : ControllerBase
{
    private readonly AppDbContext _db;

    public JobOffersController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Submit a new job offer. Requires authentication.
    /// </summary>
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

        var handler = new CreateJobOfferHandler(_db);
        var result = await handler.HandleAsync(request, userId, email, ct);

        return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, result);
    }

    /// <summary>
    /// List your own job offers. Requires authentication.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var userId = User.GetUserId()!;
        var handler = new ListJobOffersHandler(_db);
        var result = await handler.HandleAsync(userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// List all job offers (admin only).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var handler = new ListJobOffersHandler(_db);
        var result = await handler.HandleAsync(null, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get job offer details. Owner or admin only.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var isAdmin = (await HttpContext.RequestServices
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(User, "Admin")).Succeeded;

        var handler = new GetJobOfferDetailHandler(_db);
        var result = await handler.HandleAsync(id, userId, isAdmin, ct);

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Update job offer status (admin only).
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        var handler = new UpdateJobOfferStatusHandler(_db);
        var success = await handler.HandleAsync(id, request, ct);

        if (!success)
            return NotFound();

        return NoContent();
    }
}
