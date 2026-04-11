# Backend Architecture

This document explains how the backend layers fit together: which project owns what, how a request flows from HTTP through the domain to the database and back, and how errors are translated at each boundary. Read this after `docs/backend-api.md`, `docs/backend-domain.md`, and `docs/backend-db.md` if you want the bird's-eye view; read it first if you're trying to find the right place to add a new feature.

## Table of contents

- [The three projects](#the-three-projects)
- [Dependency direction](#dependency-direction)
- [Error flow](#error-flow)
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

The two-enum split (handler enum ↔ API enum) is the single most important rule for error contracts. It is restated in detail in `docs/backend-api.md` and `docs/backend-domain.md`; the short version is: domain enums may be renamed freely; API enums must not be — they are the i18n key the frontend imports.

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
