# Backend Architecture

## The projects

- **`Kalandra.Api`** — the REST boundary. Controllers, request DTOs, API error enums, REST-only response envelopes (pagination, history, stats), the Supabase bearer pipeline, rate limiting. Handles request validation and mapping. Keeps API contracts stable for the frontend.
- **`Kalandra.McpServer`** — the MCP boundary. The same domain, a different front door and a different way in: an OAuth 2.0 resource server for AI assistants. See below.
- **`Kalandra.Hosting`** — the web composition the two hosts share: Sentry + OpenTelemetry setup (parameterized by service name), the Marten store registration, `AppVersion` and the commit health check, and the `HttpContextCurrentUserAccessor`. Referenced **only** by the hosts, never by a domain project — which is exactly why this ASP.NET-coupled code lives here and not in `Kalandra.Infrastructure`, whose consumers must stay framework-light.
- **Domain projects** (e.g. `Kalandra.JobOffers`) — one project per business domain. Entities, events, command/query handlers, Marten config, and the response contracts every front door serves (`Contracts/*Response.cs`). Each domain gets its own project with vertical slices. A new domain = a new `Kalandra.{Domain}` project + `Add{Domain}Domain()` extension.
- **`Kalandra.Infrastructure`** — cross-cutting concerns. Supabase clients (auth, storage), `CurrentUser` and the claims parsing behind `ICurrentUserAccessor`, Turnstile validation, configuration records. Leaf project — depends on nothing else in the repo.

## Dependency direction

```
Kalandra.Api        ─┐
                     ├─►  Kalandra.Hosting  ─►  Kalandra.JobOffers / Kalandra.Blog  ─►  Kalandra.Infrastructure
Kalandra.McpServer  ─┘
```

The compiler enforces this via `<ProjectReference>` entries — cycles are rejected at build time. The two hosts
never reference each other: anything they share is either domain code, `Kalandra.Hosting` (web composition), or
`Kalandra.Infrastructure`. What stays in a host is what genuinely differs — its auth pipeline, its rate-limit
policies, its endpoints.

## Two hosts, one domain

The MCP server is a second front door, not a second system: its tools are thin adapters over the **same domain
handlers the controllers call**, so there is no second write path. It's a separate deployable only because it
authenticates differently — the API takes a bearer token from our own frontend, while the MCP host is an OAuth
resource server that third-party assistants connect to.

The split has one rule worth knowing: **only `Kalandra.Api` runs the Marten async daemon and the notification
subscriptions.** The MCP host registers the store so tools can read and append events, but a comment posted
through a tool is emailed exactly once, by the API's daemon reacting to the shared event store. See
`docs/mcp-server.md`.

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

## Background notifications (Marten subscriptions)

Notification emails are a side effect of committed events, delivered by Marten event subscriptions running under the async daemon — no separate workflow engine or datastore. The command handler stores the event inline like any other write (the HTTP response returns as soon as it lands); a subscription then reacts to the stored event and sends the emails. The daemon delivers each event at least once and tracks its own progress, so a comment or offer is never stored without its notifications following.

- `BlogCommentNotificationSubscription` (`Kalandra.Blog/Notifications/`) emails the blog author about every comment and the parent comment's author about replies.
- `JobOfferNotificationSubscription` (`Kalandra.JobOffers/Notifications/`) emails the site owner about new offers, and the owner and the offer's author about comments.
- **Idempotency**: sending an email isn't transactional, so each delivery records a `*NotificationSent` marker in its own transaction right after the send. The daemon replays a whole page on any failure, and the marker keeps an already-delivered email from going out twice — a failed send retries on its own.
- **Deploy safety**: both subscriptions are registered `SubscribeFromPresent` so a first deploy processes only new events (never replaying — and re-emailing — history); the daemon runs in `HotCold` mode so only one instance delivers during a blue/green overlap.
- **Email**: `IEmailSender` (`Kalandra.Infrastructure/Email/`). The real SMTP sender is registered whenever the `Email` config section exists; a logging no-op is allowed only in Development without config — production refuses to start unconfigured.

## Where to put new code

| You want to add...                                  | Goes in                                                                |
|-----------------------------------------------------|------------------------------------------------------------------------|
| A new HTTP endpoint on an existing domain           | `Kalandra.Api/Features/{Domain}/{Domain}Controller.cs`                 |
| A new request DTO or REST-only response envelope    | `Kalandra.Api/Features/{Domain}/Contracts/`                            |
| A response contract shared by REST + MCP            | `Kalandra.{Domain}/Contracts/*Response.cs`                            |
| A new domain error variant exposed on the wire      | Add to the API enum + map it in the controller `switch`               |
| A new business operation (write)                    | `Kalandra.{Domain}/Commands/{Operation}.cs` (record + enum + handler)  |
| A new read query                                    | `Kalandra.{Domain}/Queries/{Query}.cs`                                 |
| A new event                                         | `Kalandra.{Domain}/Events/{EventName}.cs` + `Apply` on the aggregate   |
| A new aggregate field that needs filtering          | Property on the entity + `Duplicate(j => j.Field)` in `MartenConfiguration` |
| A new business domain                               | New `Kalandra.{Domain}/` project + `Add{Domain}Domain()` extension     |
| A new external HTTP integration                     | `Kalandra.Infrastructure/{Concern}/` + typed `HttpClient` registration |
| A new background notification                       | `Kalandra.{Domain}/Notifications/` + a subscription registered in `AddAppMarten` |
| A new MCP tool                                      | `Kalandra.McpServer/Tools/` + call the domain handler directly (see `docs/mcp-server.md`) |
| A new role                                          | Add to `UserRole` enum + `RequireRole` policy                          |
