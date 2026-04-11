# Backend Architecture

## The three projects

- **`Kalandra.Api`** — the HTTP boundary. Controllers, request/response DTOs, API error enums, auth pipeline, rate limiting. Handles request validation and mapping. Keeps API contracts stable for the frontend.
- **Domain projects** (e.g. `Kalandra.JobOffers`) — one project per business domain. Entities, events, command/query handlers, Marten config. Each domain gets its own project with vertical slices. A new domain = a new `Kalandra.{Domain}` project + `Add{Domain}Domain()` extension.
- **`Kalandra.Infrastructure`** — cross-cutting concerns. Supabase clients (auth, storage), `CurrentUser`, Turnstile validation, configuration records. Leaf project — depends on nothing else in the repo.

## Dependency direction

```
Kalandra.Api  ───────────►  Kalandra.JobOffers  ───────────►  Kalandra.Infrastructure
      │                                                                ▲
      └────────────────────────────────────────────────────────────────┘
```

The compiler enforces this via `<ProjectReference>` entries — cycles are rejected at build time.

## Key principles

- **No mediator, no MediatR.** Controllers inject concrete handler types. The constructor signature is the explicit list of capabilities — searchable, refactorable, no magic dispatch.
- **Strongly typed configuration.** Each external dependency has a config record (`SupabaseAuthConfig`, `SupabaseStorageConfig`, `TurnstileConfig`) with its own `AddSingleton(services, configuration)` static method. The composition root never reads `IConfiguration[...]` directly.
- **Typed `HttpClient`s** for every external HTTP dependency (`IStorageService`, `ITurnstileValidator`, `ISupabaseAdminService`).
- **`TimeProvider`** is injected everywhere. Handlers and controllers read time through it; entities never read the clock. Tests substitute `FakeTimeProvider`.
- **Domain projects own their DI registration.** `ServiceRegistration.AddJobOffersDomain()` registers all handlers. The API just calls it.

## Error flow

Errors are translated at every boundary, never leaked across one:

```
Domain rule violation  →  Handler error enum  →  API error enum  →  RFC 7807 on the wire
     (internal)              (internal)            (stable)          (frontend reads as i18n key)
```

The two-enum split (handler enum ↔ API error enum) ensures domain refactoring cannot silently break the frontend. See `docs/backend-api.md` for the full rule.

## Where to put new code

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
| A new role                                          | Add to `UserRole` enum + `RequireRole` policy                          |
