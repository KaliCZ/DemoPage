# API Layer

The API layer is the HTTP boundary of the backend. It lives in `backend/src/Kalandra.Api/` and is the only project that references ASP.NET Core. Its job is to translate HTTP into handler calls, translate handler results back into HTTP responses, and enforce cross-cutting HTTP concerns (auth, rate limiting, content negotiation, problem details). **It must not contain business logic.**

> This document is the full-rationale companion to the `Key Conventions` bullets in `CLAUDE.md`. Read CLAUDE.md first for the short rules; come here when you need the why, the shape, or a concrete example.

## Table of contents

- [Project layout](#project-layout)
- [Controller responsibilities](#controller-responsibilities)
- [Vertical slice structure](#vertical-slice-structure)
- [Request & response DTOs](#request--response-dtos)
- [Error contracts: the two-enum rule](#error-contracts-the-two-enum-rule)
- [RFC 7807 validation problems](#rfc-7807-validation-problems)
- [Authorization](#authorization)
- [Rate limiting](#rate-limiting)
- [Concurrency handling](#concurrency-handling)
- [What controllers must not do](#what-controllers-must-not-do)

## Project layout

```
Kalandra.Api/
  Program.cs                # Host + pipeline wiring
  GlobalUsings.cs           # global using StrongTypes;
  Infrastructure/
    Auth/                   # JWT validation, AuthPolicies, ICurrentUserAccessor
    ControllerValidationError.cs  # ValidationError<TError>() extension
    RateLimits.cs           # Named policies + OnRejected handler
    ServiceCollectionExtensions.cs
    AuthorizeOperationFilter.cs   # Swagger decoration
    CommitHashHealthCheck.cs
  Features/
    JobOffers/
      JobOffersController.cs
      Contracts/            # Request/response DTOs and API error enums
    Auth/
      AuthController.cs
      Contracts/
    Users/
      UsersController.cs
```

Each HTTP feature lives in its own folder under `Features/`. A feature owns its controller and its `Contracts/` subfolder; it never reaches into another feature's contracts.

## Controller responsibilities

A controller action has a strict, short job:

1. **Parse** request DTOs and resolve the current user via `ICurrentUserAccessor`.
2. **Guard** with any HTTP-specific checks that cannot live in the domain (Turnstile token validation, file-size limits on `IFormFile`).
3. **Build** a command or query record for the appropriate handler.
4. **Call** the handler, `await` its `Try<TSuccess, TError>` result.
5. **Map** handler error enums onto API error enums and HTTP status codes via an exhaustive `switch` expression.
6. **Serialize** the success value to a typed response DTO.

Nothing else. No LINQ queries, no direct Marten calls, no business rules. See `Kalandra.Api/Features/JobOffers/JobOffersController.cs` for a full example.

### Example: `Create` action shape

```csharp
public async Task<ActionResult<GetJobOfferDetailResponse>> Create(
    [FromForm] CreateJobOfferRequest request,
    [FromForm] List<IFormFile>? attachments,
    [FromForm(Name = "cf-turnstile-response")] string? turnstileToken,
    CancellationToken ct)
{
    // 2. HTTP-only guard
    if (!await turnstileValidator.ValidateAsync(turnstileToken, remoteIp, ct))
        return this.ValidationError("captcha", CreateOfferError.CaptchaFailed);

    // 3. Build command
    var command = new CreateJobOfferCommand(
        User: AppUser,
        CompanyName: request.CompanyName.AsNonEmpty().Get(),
        // ...
        Timestamp: timeProvider.GetUtcNow());

    // 4. Call handler
    var result = await createHandler.HandleAsync(command, ct);

    // 5. Map errors
    if (result.IsError)
    {
        return result.Error.Get() switch
        {
            CreateJobOfferError.TooManyAttachments   => this.ValidationError("attachments", CreateOfferError.TooManyAttachments),
            CreateJobOfferError.TotalSizeTooLarge    => this.ValidationError("attachments", CreateOfferError.TotalSizeTooLarge),
            CreateJobOfferError.DisallowedContentType => this.ValidationError("attachments", CreateOfferError.DisallowedContentType),
        };
    }

    // 6. Serialize success
    var streamId = result.Success.Get();
    var offer = await getDetailHandler.HandleAsync(new GetJobOfferDetailQuery(streamId, AppUser), ct);
    return CreatedAtAction(nameof(GetDetail), new { id = streamId },
        GetJobOfferDetailResponse.Serialize(offer!, AppUser));
}
```

The `switch` expression has no default arm — the compiler enforces that every domain error is explicitly mapped. Adding a new variant to the domain enum breaks the build until the controller is updated.

## Vertical slice structure

Features are vertical slices, not horizontal layers within the API project. A feature folder under `Features/{Name}/` owns:

- `{Name}Controller.cs` — the HTTP surface for the feature
- `Contracts/*Request.cs` — inbound DTOs
- `Contracts/*Response.cs` — outbound DTOs with a static `Serialize(...)` method
- `Contracts/*Error.cs` — the API-layer error enum

There is no shared `Controllers/`, `Dtos/`, or `Models/` folder. Cross-feature reuse happens through `Kalandra.Infrastructure` (auth, storage, config) or `Kalandra.JobOffers` (domain handlers), never through a shared API-layer "common" folder.

## Request & response DTOs

- Requests are `record`s with nullable string properties that get unwrapped via `StrongTypes` (`request.CompanyName.AsNonEmpty().Get()`) when building commands. The API layer never writes its own validation attributes — invariants live in the domain.
- Responses are `record`s with a static `Serialize(entity, viewer)` method. The `viewer` parameter lets the response decide whether to expose admin-only fields. See `GetJobOfferDetailResponse.Serialize` — `AdminNotes` is returned as `null` unless `viewer.IsAdmin` is true.
- Every action declares its shape with `[ProducesResponseType<T>(StatusCodes.Status2xx)]` and `[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]`. This powers Swagger and documents the contract.

## Error contracts: the two-enum rule

**Domain error enums and API error enums are separate types.** This is the single most important rule in this layer.

- The domain/handler defines its own enum (e.g. `CreateJobOfferError`, `EditJobOfferError` in `Kalandra.JobOffers.Commands`).
- The API defines a parallel enum under `Features/{Name}/Contracts/` (e.g. `CreateOfferError`, `EditOfferError`).
- The controller maps between them via an explicit `switch`.

### Why two enums?

The API error enum is a **stable public contract** used by the frontend for direct i18n key lookup (`t('errors.CreateOffer.TooManyAttachments')`). A handler enum is an **internal implementation detail** that may be renamed when refactoring domain logic. Collapsing them would mean that renaming a domain variant silently breaks the frontend's translation keys with no compiler warning.

### What this forbids

```csharp
// ❌ BAD — leaks domain enum into the API contract
return this.ValidationError("attachments", result.Error.Get());
```

```csharp
// ✅ GOOD — explicit mapping, compiler catches any new variant
return result.Error.Get() switch
{
    CreateJobOfferError.TooManyAttachments    => this.ValidationError("attachments", CreateOfferError.TooManyAttachments),
    CreateJobOfferError.TotalSizeTooLarge     => this.ValidationError("attachments", CreateOfferError.TotalSizeTooLarge),
    CreateJobOfferError.DisallowedContentType => this.ValidationError("attachments", CreateOfferError.DisallowedContentType),
};
```

### What this also forbids

Anonymous objects as error payloads:

```csharp
// ❌ BAD — untyped contract, no Swagger, no i18n key
return BadRequest(new { error = "too_many_attachments" });
```

Controllers must never return anonymous objects. Use `this.ValidationError(field, errorEnum)` for validation failures, `NotFound()` / `Forbid()` for authorization and existence, and `Problem()` for 500s.

## RFC 7807 validation problems

All 400 responses are RFC 7807 `ValidationProblemDetails`, produced through the `ControllerExtensions.ValidationError<TError>` extension in `Kalandra.Api/Infrastructure/ControllerValidationError.cs`:

```csharp
public ActionResult ValidationError<TError>(string field, TError error)
    where TError : struct, Enum
{
    controller.ModelState.AddModelError(field, error.ToString());
    return controller.ValidationProblem();
}
```

This gives the frontend:

- `type`, `title`, `status` per RFC 7807
- `traceId` so backend logs can be correlated with the client error
- `errors[field] = [enumName]` — the enum name is the i18n key

For unexpected 500s (e.g. `SupabaseAdminService` returning an unknown failure), use `Problem()` which also emits a 7807 `ProblemDetails`. `AuthController.LinkEmail` is the canonical example.

## Authorization

- `[Authorize]` is applied at the controller class level by default; anonymous endpoints are the exception and must be annotated.
- Admin-only endpoints use `[Authorize(Policy = AuthPolicies.Admin)]`. The policy is defined in `Kalandra.Api/Infrastructure/Auth/Auth.cs` and requires the `Admin` role claim.
- Roles are projected from Supabase's JWT `app_metadata.roles` array into `ClaimTypes.Role` claims during `JwtBearerEvents.OnTokenValidated`. Only role names that parse into the `UserRole` enum are kept — unknown strings are dropped, so the `UserRole` enum is the canonical list of admissible roles.
- Per-request state is exposed through `ICurrentUserAccessor.RequiredUser`. Controllers should read it through the `AppUser` property pattern (see `JobOffersController`), not through `HttpContext.User` directly.
- JWT validation uses Supabase's OpenID Connect metadata endpoint (`{projectUrl}/auth/v1/.well-known/openid-configuration`) to fetch signing keys, with `RefreshOnIssuerKeyNotFound = true` so rotated keys are picked up automatically.

## Rate limiting

- Policies are declared in `RateLimitPolicies` (constants) and registered in `RateLimits.Add`.
- Per-policy options use `SlidingWindowRateLimiterOptions`, keyed by `user:{currentUser.Id}` — never by IP.
- Opting out: when a user has passed an interactive Turnstile challenge the client sends `X-Interactive-Captcha: 1`, which maps onto `RateLimitPartition.GetNoLimiter(...)`. This lets a legitimate user retry after a rate-limit response without waiting for the sliding window to drain.
- `OnRejected` is the only place a raw JSON string is written into the response. It deliberately uses a literal `{"error":"captcha_required"}` because this response is consumed by a matching frontend handler that triggers the interactive Turnstile flow; it is not a user-facing i18n key and is intentionally stable.

Apply a policy with `[EnableRateLimiting(RateLimitPolicies.HireMeCreateUser)]` on the action.

## Concurrency handling

Write actions that touch an event stream must be wrapped in `WithConcurrencyHandling<T>` (private helper in `JobOffersController`). It catches `Marten.ConcurrencyException` and `JasperFx.Events.EventStreamUnexpectedMaxEventIdException` and returns a `409 Conflict` with a problem-details body:

```csharp
return WithConcurrencyHandling<GetJobOfferDetailResponse>(async () =>
{
    var result = await editHandler.HandleAsync(command, ct);
    // ... map errors, return response
});
```

The controller never retries. Retries are the caller's responsibility — a retry loop inside the API layer would hide genuine race conditions from the client.

## What controllers must not do

- **No Marten calls.** Only handlers talk to `IDocumentSession` / `IQuerySession`.
- **No `DbContext`, no LINQ-over-events.** Queries live in handlers.
- **No anonymous error objects.** Use typed API error enums.
- **No business rules.** "Only the owner can edit" belongs on the entity, not in the controller. The controller only maps `NotAuthorized` to `Forbid()`.
- **No direct `HttpContext.User` reads.** Use `ICurrentUserAccessor`.
- **No reading request/response DTOs from tests.** Integration tests send anonymous objects and assert on raw `JsonElement` properties so that renaming a contract field is caught as a test failure. See the `Testing` bullet in `CLAUDE.md`.
