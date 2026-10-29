# Persistence

## Purpose

Job offers need a complete, auditable history of every change
([requirements](../product-requirements.md#job-offers)), and the blog needs durable comments,
reactions, and view counts. All of it lives in one PostgreSQL database as Marten events and
documents; uploaded files live in Supabase Storage.

## How it works

| Concern               | Provider             | Notes                                                    |
|-----------------------|----------------------|----------------------------------------------------------|
| Event store           | Marten               | PostgreSQL — Supabase in production, an Aspire-owned container locally |
| Read models           | Marten snapshots     | Same database, projected inline                          |
| Notification delivery | Marten subscriptions | Same database — daemon progress and `*NotificationSent` markers ([notifications.md](notifications.md)) |
| Identity              | Supabase Auth        | See [auth.md](auth.md)                                   |
| File attachments      | Supabase Storage     | `SupabaseStorageService`, behind `IStorageService`       |

`Kalandra.Hosting/AppMarten.cs` registers the store for both hosts; each domain project adds its
own mappings through a `Configure{Domain}()` extension on `StoreOptions`.

- **Schema** — Marten manages tables itself: `AutoCreate.All` in development, `CreateOrUpdate` in
  production (additive only, never drops). There are no migration files. Events are stored as
  JSON, so changing an event record never changes a table; the one change that needs hand-written
  SQL is removing a duplicated column, because `CreateOrUpdate` won't drop it.
- **Inline snapshots** — an aggregate's snapshot is updated in the same transaction as its events,
  so reads after a write are consistent and the two can't diverge.
- **Duplicated columns** — fields filtered on regularly (such as `Status`) are projected into real
  columns with `Duplicate(j => j.Field)`, so queries use `WHERE` instead of scanning JSON.
- **Sessions** — command handlers take an `IDocumentSession` (lightweight: no identity map, no
  change tracking) for `FetchForWriting`, appends, and `SaveChangesAsync`; query handlers take a
  read-only `IQuerySession`, so the type system stops a query from writing.
- **Streams** — an aggregate's main stream is keyed by its `Guid`. High-volume, append-only
  children get their own stream at a deterministic id derived from the parent's, which keeps them
  out of the snapshot's replay path ([conventions/domain.md](../conventions/domain.md#multiple-streams-per-aggregate)).
- **Files** — uploads go to a Supabase Storage bucket at `{userId}/{streamId}/{guid}{extension}`,
  never into PostgreSQL. If an upload fails partway through a batch, the already-uploaded files are
  deleted best-effort before the error propagates. Tests substitute `InMemoryStorageService`.

## Decisions

- **Marten on PostgreSQL** — a mature .NET event-sourcing library on the same PostgreSQL that
  Supabase already provides, so there is no extra database to run, and inline snapshots maintain
  read models with no extra code. Not a hand-rolled event store on PostgreSQL, which would need
  schema migrations as the model evolves; not MongoDB, whose transaction and concurrency semantics
  were too much unknown for this project's first event store.
- **Optimistic concurrency per stream** — `FetchForWriting<T>` records the stream version, and a
  conflicting append makes the commit throw `ConcurrencyException`, which the API turns into
  `409 Conflict`. The client decides whether to retry, so a genuine race is never hidden.

## Invariants

- Only handlers talk to Marten; controllers and tools never open a session.
- Handlers let `ConcurrencyException` bubble up to the host's conflict handling instead of
  swallowing or retrying it.
- A shipped event shape is never removed or renamed — see
  [conventions/domain.md](../conventions/domain.md#events).
- Only `Kalandra.Api` owns the schema; the MCP host runs `AutoCreate.None` in production.
