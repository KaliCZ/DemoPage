# Backend Architecture

## The three projects

- **`Kalandra.Api`** — the HTTP boundary. Controllers, request DTOs, API error enums, REST-only response envelopes (pagination, history, stats), auth pipeline, rate limiting. Handles request validation and mapping. Keeps API contracts stable for the frontend.
- **Domain projects** (e.g. `Kalandra.JobOffers`) — one project per business domain. Entities, events, command/query handlers, Marten config, and the response contracts every front door serves (`Contracts/*Response.cs`). Each domain gets its own project with vertical slices. A new domain = a new `Kalandra.{Domain}` project + `Add{Domain}Domain()` extension.
- **`Kalandra.Infrastructure`** — cross-cutting concerns. Supabase clients (auth, storage), `CurrentUser`, Turnstile validation, configuration records. Leaf project — depends on nothing else in the repo.

## Dependency direction

```
Kalandra.Api  ───────────►  Kalandra.JobOffers  ───────────►  Kalandra.Infrastructure
      │                                                                ▲
      └────────────────────────────────────────────────────────────────┘
```

The compiler enforces this via `<ProjectReference>` entries — cycles are rejected at build time.

## The MCP server (a second front door on the API)

The API host exposes a Model Context Protocol endpoint at `/mcp` in addition to the REST controllers. The MCP
tools live in `Kalandra.Api/Features/Mcp/` and are thin adapters over the **same domain handlers the
controllers call**, acting as the authenticated user — one domain, two front doors (REST + MCP), no second
write path and no separate deployable. See `docs/mcp-server.md`.

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

## Production config guards

`appsettings.json` carries dev defaults — a localhost database, the local mail catcher, Cloudflare's shared Turnstile test key, `.local` notification addresses. Production overrides each one through `kalandra-api.env`, the file the deploy writes and both slots read. So the config records end with a guard that throws at startup when a dev default survives outside Development: a misconfigured deploy dies loudly rather than quietly mailing into a catcher nobody reads or rubber-stamping every captcha. `SupabaseConfig`, `TurnstileConfig`, `EmailConfig`, `BlogFeedConfig`, `BlogNotificationsConfig`, `JobOffersNotificationsConfig`, `Observability` (Sentry) and `AddAppMarten` (the database) each have one.

**A new guard is only half the change — the deploy has to supply the key it demands.** Otherwise the new container throws on startup, crash-loops, fails the `/health/live` gate and rolls back, and every later push to `main` does the same, so the site quietly stops publishing until someone notices (that was #189). A new guard means touching:

- the `kalandra-api.env` heredoc in `.github/workflows/ci-cd.yml` — what production actually gets;
- `.github/production-boot.env` — the same key with a fake value;
- `docs/SETUP.md` §4.1, when the value comes from a GitHub secret or variable.

The `backend-production-boot` CI job keeps the first two honest. It boots the built API with `ASPNETCORE_ENVIRONMENT=Production` against exactly the keys the deploy supplies and fails unless `/health/live` answers, so a guard that outruns its deploy entry fails on the PR instead of on main.

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
| A new config key production must supply             | Config record + guard, the `kalandra-api.env` heredoc in `ci-cd.yml`, and `.github/production-boot.env` |
| A new background notification                       | `Kalandra.{Domain}/Notifications/` + a subscription registered in `AddAppMarten` |
| A new MCP tool                                      | `Kalandra.Api/Features/Mcp/` + call the domain handler directly (see `docs/mcp-server.md`) |
| A new role                                          | Add to `UserRole` enum + `RequireRole` policy                          |
