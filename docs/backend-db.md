# Database Layer

Marten lives in domain projects (e.g. `Kalandra.JobOffers`), Supabase clients live in `Kalandra.Infrastructure`, and wiring is in `Kalandra.Api/Infrastructure/ServiceCollectionExtensions.cs`.

## Storage stack

| Concern               | Provider         | Notes                                                    |
|-----------------------|------------------|----------------------------------------------------------|
| Event store           | Marten           | PostgreSQL — Supabase in prod, Docker compose locally    |
| Read models           | Marten snapshots | Same PostgreSQL instance, inline-projected               |
| Identity              | Supabase Auth    | JWT validated by API via JWKS                            |
| User admin operations | Supabase Auth    | `SupabaseAdminService` (HTTP, service-key)               |
| File attachments      | Supabase Storage | `SupabaseStorageService` (HTTP, service-key)             |

PostgreSQL is the only database. Supabase is used as managed Postgres + auth + object storage.

## Marten conventions

- **Domain projects own their Marten config.** Each domain exposes a `Configure{Domain}()` extension on `StoreOptions` (e.g. `MartenConfiguration.ConfigureJobOffers()`). The API just calls it from `AddAppMarten`.
- **`AutoCreate.All`** in dev (schema drift auto-corrected), **`CreateOrUpdate`** in prod (additive only, never drops). There are no EF-style migration files — Marten manages table schemas automatically. The only case requiring manual SQL is removing a duplicated column from a snapshot table, since `CreateOrUpdate` won't drop columns. Event shapes are stored as JSON, so changing event records doesn't affect table schema at all (see `docs/backend-domain.md` → "Events" for evolution rules).
- **Inline projections** (`SnapshotLifecycle.Inline`) — the snapshot is updated in the same transaction as the events. Read-after-write is consistent; snapshot and stream cannot diverge.
- **Duplicated columns** — fields filtered on regularly (e.g. `Status`) are projected into real Postgres columns via `Duplicate(j => j.Field)` so queries use WHERE clauses instead of scanning JSON.

## Sessions: read vs write

- **`IDocumentSession`** (read-write) — injected into command handlers. Used for `FetchForWriting`, appending events, and `SaveChangesAsync`. Lightweight sessions (no identity map / change tracking).
- **`IQuerySession`** (read-only) — injected into query handlers. Cannot modify the database (enforced by the type system).

Don't mix both in one handler.

## Concurrency control

`FetchForWriting<T>` takes a stream lock. If another request appends between fetch and save, the commit throws `ConcurrencyException`. Handlers let this bubble up — the controller's `WithConcurrencyHandling` wrapper catches it and returns `409 Conflict`.

## Event streams

- Main aggregate stream at the aggregate's `Guid` — holds submission, edits, status changes, cancellations.
- **Comment stream** at a deterministic UUID v5 via `CommentStreamId.For(jobOfferId)` — keeps high-volume comments out of the snapshot replay path. See `docs/backend-domain.md` → "Multiple streams per aggregate".

## Supabase auth integration

- JWT validation via Supabase's OpenID Connect metadata endpoint (JWKS auto-refreshed).
- `OnTokenValidated` projects `app_metadata.roles` into `ClaimTypes.Role` claims. Only values matching the `UserRole` enum are kept.
- `SupabaseAdminService` calls Supabase's REST admin API with the service key for operations the client can't perform (e.g. linking email/password to an OAuth account).

## Supabase storage integration

- File uploads go to a Supabase Storage bucket, never to Postgres.
- Path layout: `{userId}/{streamId}/{guid}{extension}`.
- Handlers depend on `IStorageService`, not on Supabase directly. Tests substitute `InMemoryStorageService`.
- If an upload fails mid-batch, `CleanupAsync` does best-effort deletion of already-uploaded files before throwing.
