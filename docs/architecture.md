# Backend Architecture

This document explains how the backend layers fit together: which project owns what, how a request flows from HTTP through the domain to the database and back, and how errors are translated at each boundary. Read this after `docs/api.md`, `docs/domain.md`, and `docs/db.md` if you want the bird's-eye view; read it first if you're trying to find the right place to add a new feature.

## Table of contents

- [The three projects](#the-three-projects)
- [Dependency direction](#dependency-direction)
- [Request flow: end to end](#request-flow-end-to-end)
- [Error flow](#error-flow)
- [DI registration](#di-registration)
- [Why this shape](#why-this-shape)
- [Where to put new code](#where-to-put-new-code)

## The three projects

```
backend/src/
  Kalandra.Api/             # ASP.NET Core host — controllers, auth pipeline, DI wiring
  Kalandra.JobOffers/       # Domain — entities, events, command/query handlers, Marten config
  Kalandra.Infrastructure/  # Cross-cutting — Supabase auth/storage clients, CurrentUser, Turnstile
```

| Project                  | Owns                                                                                          | Knows about                            |
|--------------------------|-----------------------------------------------------------------------------------------------|----------------------------------------|
| `Kalandra.Api`           | HTTP, controllers, request/response DTOs, API error enums, JWT validation, rate limiting     | All other projects                     |
| `Kalandra.JobOffers`     | Job-offer aggregate, events, decision rules, command/query handlers, Marten schema, projections | `Kalandra.Infrastructure`              |
| `Kalandra.Infrastructure`| `CurrentUser`, `IStorageService` (Supabase), `ISupabaseAdminService`, `ITurnstileValidator`, configuration records | None of the others — leaf project      |

When a new business domain is added (say, blog posts), it gets its own `Kalandra.Posts` project alongside `Kalandra.JobOffers`. The API project picks up a reference and a `services.AddPostsDomain()` call. There is no shared "Application" or "Domain.Common" project to dump things into.

## Dependency direction

```
Kalandra.Api  ───────────►  Kalandra.JobOffers  ───────────►  Kalandra.Infrastructure
      │                                                                ▲
      └────────────────────────────────────────────────────────────────┘
```

- `Kalandra.Infrastructure` is a leaf — it depends on nothing in this repo, only on NuGet packages (`Kalicz.StrongTypes`, `Supabase`, `Microsoft.Extensions.*`).
- `Kalandra.JobOffers` references `Kalandra.Infrastructure` for `CurrentUser`, `IStorageService`, etc., but knows nothing about HTTP or controllers.
- `Kalandra.Api` references both. It is the only project allowed to compose and host them.

This direction is enforced by `.csproj` `<ProjectReference>` entries — the compiler will reject any attempt to introduce a cycle.

## Request flow: end to end

Take a write path: `POST /api/job-offers` (create a job offer).

```
HTTP request
    │
    ▼
ASP.NET Core pipeline
    │  • CORS                       (Kalandra.Api/Infrastructure/ServiceCollectionExtensions.AddAppCors)
    │  • UseExceptionHandler        (Program.cs)
    │  • Authentication             (Kalandra.Api/Infrastructure/Auth/Auth.cs)
    │  • Authorization              ([Authorize] on JobOffersController)
    │  • Rate limiting              ([EnableRateLimiting(...)] policy in RateLimits.cs)
    │
    ▼
JobOffersController.Create
    │  1. Resolve CurrentUser via ICurrentUserAccessor.RequiredUser
    │  2. Validate Turnstile token (HTTP-only guard)
    │  3. Map IFormFile inputs into CreateJobOfferFile records
    │  4. Build CreateJobOfferCommand
    │
    ▼
CreateJobOfferHandler.HandleAsync                         (Kalandra.JobOffers/Commands/CreateJobOffer.cs)
    │  1. Validate count, total size, content types       → returns Try.Error<CreateJobOfferError>
    │  2. Upload attachments via IStorageService          (HTTP → Supabase Storage bucket)
    │  3. Build JobOfferSubmitted event                   (Kalandra.JobOffers/Events/JobOfferSubmitted.cs)
    │  4. session.Events.StartStream<JobOffer>(streamId, submitted)
    │  5. session.SaveChangesAsync(ct)
    │       │
    │       ▼
    │     Marten
    │       • Inserts the event row in mt_events
    │       • Runs the inline JobOffer projection in the same transaction
    │       • Inserts the snapshot row in mt_doc_joboffer with duplicated Status column
    │       • Commits PostgreSQL transaction
    │
    ▼
Handler returns Try.Success<Guid>                         (the new stream ID)
    │
    ▼
Controller
    │  • Calls GetJobOfferDetailHandler to fetch the freshly-created snapshot
    │  • Wraps it in GetJobOfferDetailResponse.Serialize(offer, viewer)
    │  • Returns 201 CreatedAtAction with the typed DTO
    │
    ▼
ASP.NET Core serializes via System.Text.Json with JsonStringEnumConverter
    │
    ▼
HTTP response
```

A read path is the same shape minus the write side. `GET /api/job-offers/{id}`:

```
Controller → GetJobOfferDetailQuery → GetJobOfferDetailHandler → IQuerySession.LoadAsync<JobOffer>
                                                                              │
                                                                              ▼
                                                                      mt_doc_joboffer (snapshot)
```

The handler enforces "owners and admins only" by returning `null` for unauthorized lookups. The controller maps `null` to `404 NotFound` so non-owners cannot tell the difference between "doesn't exist" and "not yours".

## Error flow

Errors are translated at every boundary, never leaked across one. There are four distinct error vocabularies in play:

```
┌─────────────────────────┐
│ Domain rule violation   │  e.g. JobOffer.Edit returns Try.Error(EditJobOfferError.NotSubmittedStatus)
└────────────┬────────────┘
             │  same enum value
             ▼
┌─────────────────────────┐
│ Handler error enum      │  EditJobOfferError { NotFound, NotAuthorized, NotSubmittedStatus }
│  (domain-internal)      │  EditJobOfferHandler returns Try<JobOffer, EditJobOfferError>
└────────────┬────────────┘
             │  explicit switch in controller
             ▼
┌─────────────────────────┐
│ API error enum          │  EditOfferError { NotSubmittedStatus, ... } (Features/JobOffers/Contracts)
│  (public, stable)       │  the wire-level identifier of an error
└────────────┬────────────┘
             │  ControllerExtensions.ValidationError(field, EditOfferError.NotSubmittedStatus)
             ▼
┌─────────────────────────┐
│ RFC 7807 problem detail │  ValidationProblemDetails { errors[field] = ["NotSubmittedStatus"], traceId }
│  on the wire            │  status 400, content-type application/problem+json
└────────────┬────────────┘
             │  frontend reads errors[field][0] as i18n key
             ▼
        User-facing string ("This offer can no longer be edited.")
```

Some shortcuts the controller takes for errors that don't need a stable wire-level enum:

| Domain error                                                          | Mapped to              |
|-----------------------------------------------------------------------|------------------------|
| `EditJobOfferError.NotFound`, `CancelJobOfferError.NotFound`          | `NotFound()` (404)     |
| `EditJobOfferError.NotAuthorized`, `AddCommentError.NotAuthorized`    | `Forbid()` (403)       |
| `Marten.ConcurrencyException` / `EventStreamUnexpectedMaxEventIdException` | `Conflict(...)` via `WithConcurrencyHandling` (409) |
| Unknown failures from `SupabaseAdminService`                          | `Problem()` (500, RFC 7807) |

The two-enum split (handler enum ↔ API enum) is the single most important rule for error contracts. It is restated in detail in `docs/api.md` and `docs/domain.md`; the short version is: domain enums may be renamed freely; API enums must not be — they are the i18n key the frontend imports.

## DI registration

`Program.cs` is the only place that wires services together. The order matters because some registrations read configuration that earlier registrations have parsed:

```csharp
// 1. Framework services
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddJsonOptions(...);     // JsonStringEnumConverter
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(...);                       // Bearer auth scheme + AuthorizeOperationFilter

// 2. Configuration records (singleton, parsed from IConfiguration)
var authConfig = SupabaseAuthConfig.AddSingleton(...);
SupabaseStorageConfig.AddSingleton(...);
TurnstileConfig.AddSingleton(...);

// 3. Marten + auth pipeline (need configs above)
builder.Services.AddAppMarten(...);                        // Calls options.ConfigureJobOffers()
Auth.Add(builder.Services, authConfig);
builder.Services.AddAppCors(builder.Environment);

// 4. Cross-cutting infrastructure
builder.Services.AddMemoryCache();
builder.Services.AddStorageServices();                     // typed HttpClient<IStorageService, SupabaseStorageService>
builder.Services.AddTurnstile();                           // typed HttpClient<ITurnstileValidator, ...>
builder.Services.AddAuthAdminServices();                   // typed HttpClient<ISupabaseAdminService, ...>

// 5. API request-scoped services
builder.Services.AddApiServices();                         // IHttpContextAccessor, ICurrentUserAccessor, TimeProvider

// 6. Domain handlers
builder.Services.AddJobOffersDomain();                     // Lives in Kalandra.JobOffers/ServiceRegistration.cs

// 7. Cross-cutting HTTP middleware
RateLimits.Add(builder.Services);

// 8. Health checks
builder.Services.AddHealthChecks().AddNpgSql(...).AddCheck<CommitHashHealthCheck>("version");
```

Conventions:

- **Configuration records** (`SupabaseAuthConfig`, `SupabaseStorageConfig`, `TurnstileConfig`) own their own `AddSingleton(services, configuration)` static methods. The composition root never reads `IConfiguration[...]` directly — it delegates to the config record.
- **Domain projects** own their own `Add{Domain}(...)` extension. `Kalandra.JobOffers/ServiceRegistration.cs` registers every handler in the domain. The API just calls it.
- **Typed `HttpClient`s** are used for every external HTTP dependency (`IStorageService`, `ITurnstileValidator`, `ISupabaseAdminService`). This gives them per-request lifetime, retry/circuit-breaker plug points, and shared `HttpMessageHandler` pooling without ceremony.
- **No mediator, no MediatR.** Controllers inject the concrete handler types they call. The class signature of `JobOffersController` is the explicit list of capabilities the controller has — searchable, refactorable, no magic dispatch.
- **`TimeProvider.System`** is registered as a singleton. Handlers and controllers read time through it and pass timestamps into commands; the entity never reads the clock. Tests can substitute `FakeTimeProvider`.

## Why this shape

The architecture prioritises three things over any other concern:

1. **Compiler-enforced wire stability.** Two-enum error contracts plus exhaustive `switch` expressions mean a domain refactor cannot silently break the frontend. The build either passes (and the API enums are still valid) or it fails (and you have to consciously decide what to do).
2. **Domain code that is testable without ASP.NET Core.** The domain project doesn't reference `Microsoft.AspNetCore.*`. You can write a unit test that constructs a `JobOffer`, calls `Edit(...)`, and asserts on the returned `Try` — no `WebApplicationFactory`, no `HttpClient`, no `TestServer`. Marten integration tests still spin up Postgres via Testcontainers, but that's a choice, not a requirement.
3. **Code that is greppable.** No reflection-driven dispatch. No attribute-driven validators. No `IRequestHandler<TRequest, TResponse>` you have to chase through three packages. If you want to know how a `POST /api/job-offers` works, you read `JobOffersController.Create`, then `CreateJobOfferHandler.HandleAsync`, then `JobOffer.Apply(JobOfferSubmitted)`. Three files. No surprises.

These three goals push the design toward "boring", explicit code: small handler classes, plain DI, vertical slices, no abstractions until they pay rent. That's deliberate. The complexity is in the *rules* (error contracts, event sourcing invariants, the decide/apply split), not in the *plumbing*.

## Where to put new code

A short cheat sheet for adding things:

| You want to add...                                  | Goes in                                                                |
|-----------------------------------------------------|------------------------------------------------------------------------|
| A new HTTP endpoint on an existing domain           | `Kalandra.Api/Features/{Domain}/{Domain}Controller.cs`                 |
| A new request or response shape                     | `Kalandra.Api/Features/{Domain}/Contracts/`                            |
| A new domain error variant exposed on the wire      | Add to the API enum + map it in the controller `switch`               |
| A new business operation (write)                    | `Kalandra.{Domain}/Commands/{Operation}.cs` (record + enum + handler)  |
| A new read query                                    | `Kalandra.{Domain}/Queries/{Query}.cs`                                 |
| A new event                                         | `Kalandra.{Domain}/Events/{EventName}.cs` + `Apply` on the aggregate   |
| A new aggregate field that needs filtering          | Property on the entity + `Duplicate(j => j.Field)` in `MartenConfiguration` |
| A new business domain                               | New `Kalandra.{Domain}/` project + `Add{Domain}Domain()` extension     |
| A new external HTTP integration                     | `Kalandra.Infrastructure/{Concern}/` + typed `HttpClient` registration |
| A new role                                          | Add to `UserRole` enum in `Kalandra.Infrastructure/Auth/CurrentUser.cs` + `RequireRole` in `Auth.Add` |
| A new rate-limit policy                             | Constant in `RateLimitPolicies` + factory in `RateLimits.Add`          |
| A new health check                                  | `builder.Services.AddHealthChecks().AddCheck<...>(...)` in `Program.cs` |

When in doubt, follow the pattern of an existing slice (`JobOffers` is the most complete example — it touches every layer in the system).
