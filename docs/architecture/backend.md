# Backend

The backend is one .NET solution with two hosts over shared domain projects. This doc covers how
it is split and where new code goes; the coding rules for each layer are in
[`docs/conventions/`](../conventions/).

## The projects

- **`Kalandra.Api`** — the REST boundary. Controllers, request DTOs, API error enums, REST-only response envelopes (pagination, history, stats), the Supabase bearer pipeline, rate limiting. It also runs the Marten async daemon that delivers [notifications](notifications.md).
- **`Kalandra.McpServer`** — the MCP boundary: the same domain behind a different front door, an OAuth 2.0 resource server for AI assistants. See [mcp-server.md](mcp-server.md).
- **`Kalandra.Hosting`** — the web composition both hosts share: Sentry and OpenTelemetry setup, the Marten store registration, the app version and commit health check, and the `HttpContext`-based current-user accessor. Only the hosts reference it, which is why ASP.NET-coupled code lives here rather than in `Kalandra.Infrastructure`.
- **Domain projects** (`Kalandra.JobOffers`, `Kalandra.Blog`) — one project per business domain, organized as vertical slices: entities, events, command and query handlers, Marten config, notification subscriptions, and the response contracts every front door serves.
- **`Kalandra.Infrastructure`** — cross-cutting concerns: Supabase clients (auth admin, storage), `CurrentUser` and claims parsing, Turnstile validation, email sending, configuration records. A leaf project that depends on nothing else in the repo.

## Dependency direction

```
Kalandra.Api        ─┐
                     ├─►  Kalandra.Hosting  ─►  Kalandra.JobOffers / Kalandra.Blog  ─►  Kalandra.Infrastructure
Kalandra.McpServer  ─┘
```

`<ProjectReference>` entries enforce this; the compiler rejects cycles. The two hosts never
reference each other: what they share is domain code, `Kalandra.Hosting`, or
`Kalandra.Infrastructure`. A host keeps only what genuinely differs — its auth pipeline, its
rate-limit policies, its endpoints.

## Two hosts, one domain

The MCP server is a second front door, not a second system: its tools are thin adapters over the
same domain handlers the controllers call, so there is no second write path. It is a separate
deployable only because it authenticates differently — the API takes a bearer token from the
site's own frontend, while the MCP host is an OAuth resource server that third-party assistants
connect to.

## Error flow

Errors are translated at every boundary, never leaked across one:

```
Domain rule violation  →  Handler error enum  →  API error enum  →  RFC 7807 on the wire
     (internal)              (internal)            (stable)          (frontend reads as i18n key)
```

The two-enum split keeps a domain refactor from silently breaking the frontend's translation keys.
The rule is in [conventions/api.md](../conventions/api.md#error-contracts-the-two-enum-rule).

## Decisions

- **Vertical slices, not layers** — a feature's controller, contracts, events, and handlers live
  together, so adding a feature adds a folder instead of touching `Controllers/`, `Services/`,
  `Models/`, and `Dtos/`. A new domain is a new `Kalandra.{Domain}` project with its own
  `Add{Domain}Domain()` registration.
- **No mediator, no MediatR** — controllers and tools inject concrete handler types, so a
  constructor is the explicit list of what an endpoint can do: searchable, refactorable, no magic
  dispatch.
- **Strongly typed configuration** — each external dependency has a config record with its own
  registration method; the composition root never reads `IConfiguration[...]` directly.
- **Typed `HttpClient`s** for every external HTTP dependency, behind an interface the tests can
  replace.
- **`TimeProvider` everywhere** — handlers and controllers read time through it and entities never
  read the clock, so tests control time with `FakeTimeProvider`.

## Where to put new code

| You want to add...                                  | Goes in                                                                |
|-----------------------------------------------------|------------------------------------------------------------------------|
| A new HTTP endpoint on an existing domain           | `Kalandra.Api/Features/{Domain}/{Domain}Controller.cs`                 |
| A new request DTO or REST-only response envelope    | `Kalandra.Api/Features/{Domain}/Contracts/`                            |
| A response contract shared by REST + MCP            | `Kalandra.{Domain}/Contracts/*Response.cs`                             |
| A new domain error variant exposed on the wire      | Add to the API enum + map it in the controller `switch`                |
| A new business operation (write)                    | `Kalandra.{Domain}/Commands/{Operation}.cs` (record + enum + handler)  |
| A new read query                                    | `Kalandra.{Domain}/Queries/{Query}.cs`                                 |
| A new event                                         | `Kalandra.{Domain}/Events/{EventName}.cs` + `Apply` on the aggregate   |
| A new aggregate field that needs filtering          | Property on the entity + `Duplicate(j => j.Field)` in `MartenConfiguration` |
| A new business domain                               | New `Kalandra.{Domain}/` project + `Add{Domain}Domain()` extension     |
| A new external HTTP integration                     | `Kalandra.Infrastructure/{Concern}/` + typed `HttpClient` registration |
| A new background notification                       | `Kalandra.{Domain}/Notifications/` + a subscription registered in `AddAppMarten` (see [notifications.md](notifications.md)) |
| A new MCP tool                                      | `Kalandra.McpServer/Tools/`, calling the domain handler directly (see [mcp-server.md](mcp-server.md)) |
| A new role                                          | Add to the `UserRole` enum + a `RequireRole` policy                    |

## Invariants

- Domain projects never reference ASP.NET Core; anything HTTP-shaped stays in a host or
  `Kalandra.Hosting`.
- Neither host references the other.
- Only `Kalandra.Api` runs the async daemon and the notification subscriptions; the MCP host only
  reads and appends events, and the API's daemon emails whatever a tool stored.
