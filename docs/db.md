# Database Layer

The database layer covers everything that touches PostgreSQL or Supabase: Marten configuration, event-store schema, projections, and the Supabase auth/storage integrations. There is no separate "Db" project — Marten lives in `Kalandra.JobOffers` (the domain that owns the events) and Supabase clients live in `Kalandra.Infrastructure`. The wiring of both is in `Kalandra.Api/Infrastructure/ServiceCollectionExtensions.cs`.

> This document expands on the `Event sourcing` and `Auth` bullets in `CLAUDE.md` and covers the database side of the rules in `docs/domain.md`.

## Table of contents

- [Storage stack](#storage-stack)
- [Marten configuration](#marten-configuration)
- [`AutoCreate` modes](#autocreate-modes)
- [Sessions: read vs write](#sessions-read-vs-write)
- [Inline projections](#inline-projections)
- [Event streams](#event-streams)
- [Concurrency control](#concurrency-control)
- [Querying read models](#querying-read-models)
- [Connection strings](#connection-strings)
- [Supabase auth integration](#supabase-auth-integration)
- [Supabase storage integration](#supabase-storage-integration)
- [Health checks](#health-checks)

## Storage stack

| Concern               | Provider         | Lives in                                                  |
|-----------------------|------------------|-----------------------------------------------------------|
| Event store           | Marten           | PostgreSQL — Supabase in prod, Docker compose locally     |
| Read models           | Marten snapshots | Same PostgreSQL instance, inline-projected                |
| Identity              | Supabase Auth    | Supabase project (JWT validated by API via JWKS)          |
| User admin operations | Supabase Auth    | `SupabaseAdminService` (HTTP, service-key)                |
| File attachments      | Supabase Storage | `SupabaseStorageService` (HTTP, service-key)              |
| Health checks         | Npgsql           | `AspNetCore.HealthChecks.NpgSql` against `DefaultConnection` |

PostgreSQL is the only database. Supabase is used as managed Postgres + auth + object storage; nothing in the API code talks to Supabase as a separate datastore — auth and storage are HTTP-mediated, and Marten talks straight to the underlying Postgres via the connection string.

## Marten configuration

All Marten setup goes through `Kalandra.Api.Infrastructure.ServiceCollectionExtensions.AddAppMarten`:

```csharp
services.AddMarten(options =>
{
    options.Connection(connectionString);

    // Domain-specific Marten configuration
    options.ConfigureJobOffers();

    options.UseSystemTextJsonForSerialization();

    if (environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
    else
    {
        options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
    }
})
.UseLightweightSessions();
```

Per-domain configuration lives next to the domain it describes. `JobOffers` exposes a `ConfigureJobOffers()` extension on `StoreOptions` in `Kalandra.JobOffers/MartenConfiguration.cs`:

```csharp
public static void ConfigureJobOffers(this StoreOptions options)
{
    options.Projections.Snapshot<JobOffer>(SnapshotLifecycle.Inline);
    options.Schema.For<JobOffer>().Duplicate(j => j.Status);
}
```

Two things to notice:

1. **Marten config for a domain lives in the domain project, not in `Kalandra.Api`.** The API project knows nothing about `JobOffer`'s schema. If a new domain is added, it ships its own `Configure{Domain}()` extension and the API just calls it from `AddAppMarten`.
2. **Duplicated columns** — `j.Status` is projected into a real Postgres column so the index can satisfy filters in `ListJobOffers` (`q.Where(j => j.Status.In(query.Statuses))`) without scanning JSON. Add `Duplicate(...)` for any field that the read model filters on regularly.

## `AutoCreate` modes

| Environment | Mode | Behavior |
|-------------|------|----------|
| Development | `AutoCreate.All`            | Marten can create or drop tables/columns at will. Schema drift is corrected automatically on startup. |
| Production  | `AutoCreate.CreateOrUpdate` | Marten will create new tables and add new columns, but never drops anything. Destructive changes have to be made by hand. |

The split exists because dev iteration benefits from "just make it match" while production must never lose data. There is no `AutoCreate.None` step today — when the schema diverges in production, the safe path is a manual SQL migration.

## Sessions: read vs write

Marten exposes two session types and we use both deliberately:

- **`IDocumentSession`** (read-write) is injected into command handlers. It is used to load aggregates with `FetchForWriting`, append events, and call `SaveChangesAsync`. `UseLightweightSessions()` is set on the store, so sessions skip the identity-map / change-tracking machinery — entities loaded from a write session do not auto-save when modified, which matches the explicit "decide → append → apply → commit" flow used by handlers.
- **`IQuerySession`** (read-only) is injected into query handlers. It cannot modify the database, which is enforced by the type system rather than convention.

A handler that needs to write events uses `IDocumentSession`. A handler that only reads should request `IQuerySession`. Mixing the two in one handler is a smell.

## Inline projections

`SnapshotLifecycle.Inline` means the `JobOffer` snapshot is updated **in the same transaction** as the events that produce it. This has two important consequences:

1. **Read-after-write is consistent.** Right after `SaveChangesAsync`, a `LoadAsync<JobOffer>` from any session sees the new state. There is no eventual-consistency window.
2. **The snapshot and the stream cannot diverge.** A failure to write the snapshot rolls back the events too.

Inline projections are the right default for low-volume aggregates where the snapshot is small. For high-volume aggregates, async projections (`SnapshotLifecycle.Async`) trade read latency for write throughput — but we don't need that here.

## Event streams

Streams are the unit of consistency in Marten:

- A `JobOffer` stream is started in `CreateJobOfferHandler` with `session.Events.StartStream<JobOffer>(streamId, submittedEvent)`. The stream ID is the aggregate's `Guid`.
- Subsequent edits, status changes, and cancellations append onto that same stream via `stream.AppendOne(evt)` after `FetchForWriting<JobOffer>`.
- **Comments live on a separate stream**, derived deterministically from the aggregate ID with `CommentStreamId.For(jobOfferId)` (UUID v5, SHA-1 namespace hash). This keeps high-volume comment events out of the snapshot replay path. See `docs/domain.md` → "Multiple streams per aggregate" for the rationale.

When you need a related collection that can grow unboundedly, follow the comment-stream pattern: a deterministic UUID v5 derived from the parent's ID via a fixed namespace Guid.

## Concurrency control

`session.Events.FetchForWriting<JobOffer>(id, ct)` takes the stream lock that Marten uses to detect concurrent writes. If another request appends to the same stream between `FetchForWriting` and `SaveChangesAsync`, the commit throws `ConcurrencyException` (or `EventStreamUnexpectedMaxEventIdException` from JasperFx).

The handler does **not** catch these exceptions. The controller's `WithConcurrencyHandling<T>` wrapper catches them and returns `409 Conflict` with a problem-details body. This split is deliberate: concurrency is an HTTP concern (it maps to a status code), not a domain concern.

```csharp
catch (Exception ex) when (ex is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
{
    return Conflict(ProblemDetailsFactory.CreateProblemDetails(
        HttpContext,
        statusCode: StatusCodes.Status409Conflict,
        detail: "Job offer was modified by another request."));
}
```

## Querying read models

Query handlers use the snapshot, not the event stream:

```csharp
var offer = await session.LoadAsync<JobOffer>(query.Id, ct);
```

LINQ over snapshots goes through `session.Query<JobOffer>()`. Marten translates this into SQL against the `mt_doc_joboffer` table; filters on duplicated columns (like `Status`) become real WHERE clauses, while filters on JSON fields use Postgres' JSONB operators.

For history views, you can `FetchStreamAsync(id, ct)` to read all events on a stream (used by `GetJobOfferHistoryHandler` to render a chronological audit log). This is the only place where reads bypass the snapshot.

```csharp
var offerEvents = await session.Events.FetchStreamAsync(query.Id, token: ct);
var commentEvents = await session.Events.FetchStreamAsync(CommentStreamId.For(query.Id), token: ct);
```

## Connection strings

The Postgres connection string lives in `appsettings.json` under `ConnectionStrings:DefaultConnection`. Locally it points at the Docker compose Postgres in `backend/docker-compose.yml`:

```yaml
db:
  image: postgres:17-alpine
  ports: ["5432:5432"]
  environment:
    POSTGRES_USER: kalandra
    POSTGRES_PASSWORD: kalandra_dev
    POSTGRES_DB: kalandra
```

In production it points at the Supabase project's Postgres connection. There is no second connection — Marten and the health check both use `DefaultConnection`.

Tests use Testcontainers to spin up a fresh Postgres per test class, so they exercise real schema creation paths. See the `Testing` bullet in `CLAUDE.md` for the wire-contract assertion rules that apply to those tests.

## Supabase auth integration

Authentication state is stored in Supabase. The backend never holds password hashes or session rows; it only validates JWTs.

- **Configuration**: `SupabaseAuthConfig` (project URL + service key) is registered in `Program.cs` via `SupabaseAuthConfig.AddSingleton(...)`.
- **Validation**: `Auth.Add` configures `AddJwtBearer` with `MetadataAddress = {projectUrl}/auth/v1/.well-known/openid-configuration`. Signing keys come from Supabase's JWKS endpoint and are auto-refreshed via `RefreshOnIssuerKeyNotFound = true`.
- **Issuer / audience**: `ValidIssuer = {projectUrl}/auth/v1`, `ValidAudience = "authenticated"` — these are Supabase's defaults.
- **Role projection**: `OnTokenValidated` parses `app_metadata.roles` and adds a `ClaimTypes.Role` claim for each value that maps to the `UserRole` enum. Unknown role names are dropped.
- **User construction**: `HttpContextCurrentUserAccessor` reads `sub`, `email`, and `user_metadata` (full name, avatar URL) to build the request-scoped `CurrentUser`.
- **Admin operations**: `SupabaseAdminService` (in `Kalandra.Infrastructure/Auth`) calls Supabase's REST admin API with the service key for operations the client can't perform itself, e.g. linking an email/password identity to an existing OAuth account.

## Supabase storage integration

File attachments live in a Supabase Storage bucket — never in Postgres.

- **Configuration**: `SupabaseStorageConfig` (project URL + bucket name + service key) is loaded from `appsettings.json` `Storage` section.
- **Implementation**: `SupabaseStorageService` is the only `IStorageService` implementation. It is registered with `AddHttpClient<IStorageService, SupabaseStorageService>()` so it gets a typed `HttpClient` from `IHttpClientFactory`.
- **Path layout**: uploads under `{userId}/{streamId}/{guid}{extension}`. The user ID is the top-level partition so per-user clean-up is straightforward.
- **Atomicity**: `UploadAsync` uploads files sequentially. If any upload fails, `CleanupAsync` does best-effort deletion of the already-uploaded files before throwing `StorageUploadException`. This isn't a transaction — Supabase Storage doesn't support one — but it prevents orphaned files in the common failure mode.
- **Domain decoupling**: handlers depend on `IStorageService`, not on Supabase. Tests substitute a fake implementation.
- **Download**: `DownloadAsync` returns a streaming `StorageDownloadResult` (with `IAsyncDisposable`) so attachments can be range-streamed back to the client without buffering in memory.

## Health checks

`Program.cs` registers two health checks:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddCheck<CommitHashHealthCheck>("version");
```

- **`AddNpgSql`** — verifies the Postgres connection string can be opened. This catches connection-string typos, network issues, and Supabase outages.
- **`CommitHashHealthCheck`** — surfaces the embedded `SourceRevisionId` from the assembly's `AssemblyInformationalVersion`, set at build time via `dotnet build -p:SourceRevisionId=$(git rev-parse HEAD)`. Useful for confirming what's actually deployed.

The endpoint is mounted at `/health` and serializes via `HealthChecks.UI.Client.UIResponseWriter` for a structured JSON response.
