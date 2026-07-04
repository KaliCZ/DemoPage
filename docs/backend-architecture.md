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
- **Strongly typed configuration.** Each external dependency has a config record (`SupabaseConfig`, `TurnstileConfig`) with its own `AddSingleton(services, configuration)` static method. The composition root never reads `IConfiguration[...]` directly.
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

## Background workflows (Temporal)

Blog comments are the first Temporal-backed flow: `BlogCommentWorkflow` (`Kalandra.Blog/Workflows/`) stores the comment and sends the notification emails as two activities of one durable workflow, so neither can happen without the other. The API process hosts the worker (`AddTemporal` in `ServiceCollectionExtensions`); the controller drives the workflow with update-with-start — the HTTP response returns as soon as the store activity lands, notifications continue asynchronously with retries. Activities must be idempotent (retried on failure); the store dedupes by comment id.

- **Dev server**: Aspire runs `temporalio/temporal server start-dev` (also started by `npm run test:e2e` and the CI e2e job); integration tests spin one per test class via Testcontainers.
- **Production**: `temporalio/auto-setup` on the VM, persistence in the same Supabase Postgres (`temporal` + `temporal_visibility` databases), deployed by the one-off `deploy-temporal` workflow. The Web UI listens on :8233 (SSH tunnel).
- **Email**: `IEmailSender` (`Kalandra.Infrastructure/Email/`). The real SMTP sender is registered whenever the `Email` config section exists; a logging no-op is allowed only in Development without config — production refuses to start unconfigured.

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
| A new background workflow                           | `Kalandra.{Domain}/Workflows/` + register it on the worker in `AddTemporal` |
| A new role                                          | Add to `UserRole` enum + `RequireRole` policy                          |
