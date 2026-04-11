# Domain Layer

The domain layer owns the business. It lives in `backend/src/Kalandra.JobOffers/` and is the only place where business rules, invariants, and event shapes are defined. It has no reference to ASP.NET Core and does not know what HTTP is. Its dependencies are `Marten` (event store) and `Kalandra.Infrastructure` (cross-cutting types like `CurrentUser`, `IStorageService`).

> This document expands on the `Key Conventions` bullets in `CLAUDE.md` that cover event sourcing, handlers, and domain error enums.

## Table of contents

- [Project layout](#project-layout)
- [The handler pattern](#the-handler-pattern)
- [Commands, queries, handlers](#commands-queries-handlers)
- [The `Try<TSuccess, TError>` return type](#the-trytsuccess-terror-return-type)
- [Entities and event sourcing](#entities-and-event-sourcing)
- [Events](#events)
- [Multiple streams per aggregate](#multiple-streams-per-aggregate)
- [Domain error enums](#domain-error-enums)
- [Dependency injection](#dependency-injection)
- [What the domain must not do](#what-the-domain-must-not-do)

## Project layout

```
Kalandra.JobOffers/
  Commands/
    CreateJobOffer.cs           # record + enum + handler, all in one file
    EditJobOffer.cs
    CancelJobOffer.cs
    UpdateJobOfferStatus.cs
    AddComment.cs
  Queries/
    GetJobOfferDetail.cs
    ListJobOffers.cs
    GetJobOfferHistory.cs
    ListComments.cs
    GetAttachmentInfo.cs
  Entities/
    JobOffer.cs                 # Aggregate + Apply methods + domain error enums
    JobOfferStatus.cs
    AttachmentInfo.cs
  Events/
    JobOfferSubmitted.cs
    JobOfferEdited.cs
    JobOfferCancelled.cs
    JobOfferStatusChanged.cs
    JobOfferCommentAdded.cs
  MartenConfiguration.cs        # ConfigureJobOffers() extension on StoreOptions
  CommentStreamId.cs            # Deterministic UUID v5 for comment stream
  ServiceRegistration.cs        # AddJobOffersDomain() DI registration
```

Each command file is self-contained: the command record, its error enum, and its handler live together. When reading the code, you only need to open one file to understand a single operation. Queries follow the same rule.

## The handler pattern

Every command or query has a handler class with a single public method: `HandleAsync(command, ct)`. Handlers own:

1. **Validation**: invariants that require loading state (e.g. "attachments are ≤ 5" in `CreateJobOffer`, "status is Submitted" via `JobOffer.Edit`).
2. **Orchestration**: loading the aggregate or event stream, delegating to the entity, persisting changes.
3. **Side effects**: uploading attachments through `IStorageService`, writing events to Marten, committing the session.

Handlers do **not** own:

- Presentation (no DTOs — handlers return domain entities or event objects)
- Authorization translation (they return `NotAuthorized` as a domain error; the controller translates that to 403)
- HTTP-specific guards (Turnstile, rate limits, content-size caps on `IFormFile`)

A handler's validation is the **last line of defence**. The API layer may reject bad input earlier for UX reasons, but a handler that assumes its inputs have already been validated is a bug.

### Example handler shape

```csharp
public class EditJobOfferHandler(IDocumentSession session)
{
    public async Task<Try<JobOffer, EditJobOfferError>> HandleAsync(
        EditJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<JobOffer, EditJobOfferError>(EditJobOfferError.NotFound);

        var result = offer.Edit(
            user: command.User,
            companyName: command.CompanyName.Value,
            // ...
            timestamp: command.Timestamp);

        if (result.IsError)
            return Try.Error<JobOffer, EditJobOfferError>(result.Error.Get());

        var evt = result.Success.Get();
        stream.AppendOne(evt);
        offer.Apply(evt);
        await session.SaveChangesAsync(ct);
        return Try.Success<JobOffer, EditJobOfferError>(offer);
    }
}
```

Note the shape:

1. Load via `FetchForWriting<T>` — this takes the stream lock that Marten uses to detect concurrency conflicts on `SaveChangesAsync`.
2. Delegate the "can I do this?" check to the entity (`offer.Edit(...)`).
3. If the entity returns an event, append it, apply it to the in-memory aggregate, and commit.

The handler does not decide whether the edit is allowed; the entity does. The handler's only responsibility is to translate the decision into a stream append.

## Commands, queries, handlers

- **Commands** mutate state. Their return type is `Try<TSuccess, TError>` where `TSuccess` is either the new stream ID (on create) or the updated aggregate (on edit/cancel/change-status). They take an `IDocumentSession` (read-write).
- **Queries** read state. They return `T?` (null for not-found or not-authorized) or a result record. They take an `IQuerySession` (read-only).
- Both are registered as `Scoped` in `ServiceRegistration.AddJobOffersDomain()`.
- The command / query / handler live in the **same file** with matching names: `CreateJobOffer.cs` holds `CreateJobOfferCommand`, `CreateJobOfferError`, and `CreateJobOfferHandler`.

### Queries enforce access control

```csharp
public async Task<JobOffer?> HandleAsync(GetJobOfferDetailQuery query, CancellationToken ct)
{
    var offer = await session.LoadAsync<JobOffer>(query.Id, ct);
    if (offer == null)
        return null;

    if (!query.User.IsAdmin && offer.UserId != query.User.Id)
        return null;  // Treat "not yours" as "not found" — no information leak

    return offer;
}
```

Queries that shouldn't expose an aggregate's existence return `null` (not a domain error enum) when the user isn't authorized to see it. The controller maps `null` to `404 NotFound`, so non-owners cannot distinguish "doesn't exist" from "not mine".

## The `Try<TSuccess, TError>` return type

`Try<TSuccess, TError>` is a discriminated-union-like result type from the `Kalicz.StrongTypes` NuGet package (made available via `global using StrongTypes;` in `GlobalUsings.cs`). Use it consistently for handler and entity methods whose "failure" is a business decision, not an exception.

- `Try.Success<TSuccess, TError>(value)` — successful case.
- `Try.Error<TSuccess, TError>(error)` — business-logic failure.
- `result.IsError`, `result.Success.Get()`, `result.Error.Get()` — interrogation.

Exceptions are reserved for truly exceptional conditions — storage failures, DB outages, concurrency conflicts, programmer errors. A user who tries to edit someone else's job offer is not an exception; it is a `Try.Error<..., EditJobOfferError>(NotAuthorized)`.

## Entities and event sourcing

Aggregates are plain C# classes with:

- **Public state** — mutable `{ get; set; }` properties that reflect the current projection. Marten rehydrates these via `Apply(...)` methods.
- **Decision methods** — `Edit`, `Cancel`, `ChangeStatus`, etc. Each returns a `Try<TEvent, TError>`: if the operation is allowed, it returns the event that describes the state change; if not, it returns a domain error. **Decision methods do not mutate the aggregate.** Mutation happens only in `Apply`.
- **Apply methods** — one `Apply(TEvent)` per event type. These are the only methods that mutate state. Marten calls them during snapshot rebuilds; handlers also call them manually after appending, so the in-memory aggregate stays in sync without a reload.

### Why this split?

Separating "decide" from "apply" means:

- The same event that is appended to the stream is the one that rebuilds state. The two cannot drift.
- `Apply` can be tested in isolation by constructing an event and checking the resulting state.
- `Edit` / `Cancel` / `ChangeStatus` can be tested by constructing an aggregate in a given state and asserting on the returned `Try`, without touching Marten at all.
- Marten's snapshot projection (`SnapshotLifecycle.Inline`) can rebuild the aggregate from events on load, guaranteeing that persisted state and derived state are identical.

### Example

```csharp
public Try<JobOfferEdited, EditJobOfferError> Edit(
    CurrentUser user, string companyName, /* ... */, DateTimeOffset timestamp)
{
    if (UserId != user.Id)
        return Try.Error<JobOfferEdited, EditJobOfferError>(EditJobOfferError.NotAuthorized);

    if (Status != JobOfferStatus.Submitted)
        return Try.Error<JobOfferEdited, EditJobOfferError>(EditJobOfferError.NotSubmittedStatus);

    return Try.Success<JobOfferEdited, EditJobOfferError>(new JobOfferEdited(
        EditedByUserId: user.Id,
        CompanyName: companyName,
        // ...
        Timestamp: timestamp));
}

public void Apply(JobOfferEdited e)
{
    CompanyName = e.CompanyName;
    // ...
    UpdatedAt = e.Timestamp;
}
```

### Status transitions

State machines belong on the entity. `JobOffer.CanTransitionTo(JobOfferStatus)` is private and used inside `ChangeStatus`. The handler never switches on `offer.Status` to decide whether a transition is legal — that's the entity's job.

## Events

Events are `record`s with only immutable data:

```csharp
public record JobOfferSubmitted(
    Guid UserId,
    string UserEmail,
    string CompanyName,
    // ...
    IReadOnlyList<AttachmentInfo> Attachments,
    DateTimeOffset Timestamp);
```

Rules:

- **Past tense naming** — `JobOfferSubmitted`, `JobOfferEdited`, `JobOfferCancelled`. An event is a record of something that happened, not a command.
- **Self-contained** — events carry every field needed to rebuild the aggregate state they touch. Do not reference other entities by ID and expect them to be loaded lazily.
- **Additive evolution** — once an event shape ships, it lives forever in the store. Adding a new nullable field is safe; removing or renaming one breaks rehydration of historical streams. When a genuinely incompatible change is needed, introduce a new event type (`JobOfferEditedV2`) and leave the original intact.
- **Timestamps come from the command** — handlers resolve `TimeProvider.GetUtcNow()` in the API layer and pass it through the command. The entity does not read the clock.
- **Actor tracking** — events that are triggered by an authenticated user carry `UserId` / `UserEmail` (or `EditedByUserId`, `CancelledByUserId`, etc.) so the history is auditable.

## Multiple streams per aggregate

`JobOffer` owns two streams:

1. The main event stream at the aggregate's own `Guid` — holds submission, edits, status changes, cancellations.
2. A **comment stream** at a deterministic UUID v5 derived from the aggregate ID via `CommentStreamId.For(jobOfferId)`.

Comments are kept separate because they are high-volume, append-only, and do not affect the main aggregate's state. Putting them on the main stream would bloat the snapshot and force every rebuild to replay every comment. `AddCommentHandler` writes to `CommentStreamId.For(...)`, not to the aggregate's stream:

```csharp
session.Events.Append(CommentStreamId.For(command.JobOfferId), commentEvent);
```

When you need a similar separate stream for a different concept, follow the same pattern: a static helper that hashes the parent's ID with a fixed namespace Guid so the derived ID is stable and collision-free.

## Domain error enums

Each feature defines its own error enum next to its handler or entity. Examples:

- `CreateJobOfferError { TooManyAttachments, TotalSizeTooLarge, DisallowedContentType }` — in `Commands/CreateJobOffer.cs`
- `EditJobOfferError { NotFound, NotAuthorized, NotSubmittedStatus }` — in `Entities/JobOffer.cs` (used by both `JobOffer.Edit` and `EditJobOfferHandler`)
- `AddCommentError { NotFound, NotAuthorized }` — in `Entities/JobOffer.cs`

These enums are **internal to the domain**. They may be renamed, split, or merged freely when refactoring — the API layer's parallel enums shield the frontend from those changes (see `docs/backend-api.md` → "Error contracts: the two-enum rule"). That freedom is the whole point of keeping them separate: domain refactoring should never break the wire contract.

## Dependency injection

Handlers are registered in `ServiceRegistration.AddJobOffersDomain()`, which is called from `Program.cs`:

```csharp
public static IServiceCollection AddJobOffersDomain(this IServiceCollection services)
{
    services.AddScoped<CreateJobOfferHandler>();
    services.AddScoped<EditJobOfferHandler>();
    // ...
    services.AddScoped<GetJobOfferDetailHandler>();
    // ...
    return services;
}
```

- Handlers are `Scoped` because they depend on `IDocumentSession` / `IQuerySession`, which are themselves scoped per request.
- There is no mediator, no pipeline, no `IRequestHandler<TRequest, TResponse>`. Controllers inject the concrete handler types they need. A `JobOffersController` constructor lists every handler it uses, which makes the surface of each controller explicit and greppable.

## What the domain must not do

- **No `IHttpContextAccessor`, no `HttpContext`, no `ControllerBase`.** The domain project does not reference ASP.NET Core.
- **No response DTOs.** Handlers return domain entities or raw event records. DTO shaping belongs in the API layer.
- **No direct database queries outside of handlers.** No static `JobOffer.LoadFromDb(...)` helpers. The entity is pure; Marten is the only persistence mechanism, and only handlers call it.
- **No `DateTime.UtcNow`.** Clocks are always injected as `TimeProvider` and the timestamp is passed through the command.
- **No throwing for business failures.** Use `Try<..., DomainError>`. Throw only for programmer errors and infrastructure faults.
- **No swallowing `ConcurrencyException`.** The handler commits with `SaveChangesAsync` and lets concurrency exceptions bubble up to the controller's `WithConcurrencyHandling` wrapper.
