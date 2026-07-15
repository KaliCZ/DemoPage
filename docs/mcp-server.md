# MCP Server

`Kalandra.McpServer` is a standalone host that serves a [Model Context Protocol](https://modelcontextprotocol.io)
endpoint at **`https://mcp.kalandra.tech/mcp`** (streamable HTTP, stateless). Anonymous callers can browse blog
posts and read their comments; signed-in callers can also act on kalandra.tech as their account — submit and
follow up on job offers, and write comments.

It is its own deployable, separate from `Kalandra.Api`, because the two authenticate differently: **the REST
API takes a Supabase bearer token from the site's own frontend; the MCP server is an OAuth resource server**
that third-party assistants connect to without ever seeing a credential.

## Architecture: two hosts, one domain

Both hosts are thin front doors over the **same domain handlers and the same Marten store**:

```
Kalandra.Api      (REST, bearer)  ─┐
                                   ├─►  domain handlers  ─►  Marten (events)  ─►  notification subscriptions (email)
Kalandra.McpServer (MCP, OAuth)   ─┘    (CreateJobOffer, PostBlogComment, …)      (Kalandra.Api only)
```

- **Same logic as the UI.** A tool builds the same command/query record a controller builds and calls the same
  handler. Validation and the event store behave identically — there is no second write path.
- **Only the API runs the async daemon.** `Kalandra.McpServer` registers the Marten *store* so tools can read
  and append events, but deliberately not `AddAsyncDaemon` or the notification subscriptions. A comment posted
  through a tool is therefore emailed exactly once — by the API host's daemon reacting to the shared event
  store. It also runs `AutoCreate.None` in production: the API owns the schema.
- **Shared code, not a shared host.** The response contracts (`GetJobOfferDetailResponse`, `CommentResponse`,
  `BlogCommentResponse`) and the blog feed live in the domain slices; `ICurrentUserAccessor` and the
  claims→`CurrentUser` parsing live in `Kalandra.Infrastructure`. Each host owns its own pipeline.
- **Blog posts come from the RSS feed** (`Kalandra.Blog/Feed/BlogFeedClient`), because the backend's post
  catalog holds only slugs and stream ids — the frontend owns post titles and summaries.

## Authentication: Supabase is the authorization server

The MCP server is an **OAuth 2.0 resource server**. It issues no tokens and owns no credentials; Supabase's
OAuth 2.1 server runs the whole flow.

```
Assistant ──1── POST /mcp (no token)  →  anonymous tier: whole toolset listed, public blog tools callable
          ──2── tools/call on an [Authorized] tool  →  401 + WWW-Authenticate: resource_metadata=…
          ──3── GET /.well-known/oauth-protected-resource  →  { resource, authorization_servers: [<supabase>/auth/v1], scopes_supported }
          ──4── OAuth 2.1 + PKCE against Supabase (discovery, client registration, consent)
          ──5── POST /mcp with the access token  →  the full toolset, acting as that user
```

- **A tool call that needs an account is challenged, not refused.** The
  [authorization spec](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization) puts
  authorization at the transport level and requires HTTP status codes for it — 401 for "authorization required
  or token invalid". That is the only refusal a client's OAuth code acts on: it reads `WWW-Authenticate`, finds
  the resource metadata, and signs in or refreshes on its own. A JSON-RPC error or a tool error saying "please
  sign in" reaches only the model, which can do nothing but relay it to the user.
- **`McpAccountGate` issues that challenge**, as middleware ahead of the endpoint. It has to sit there: the SDK
  cannot challenge from inside a tool, because by then the response is already a JSON-RPC envelope, and stock
  `RequireAuthorization` would shut the anonymous tier out of the whole endpoint while never seeing the body
  that says which tool is wanted. It owns the list of public tools, so a new tool is account-only until named
  there; `ToolAuthorizationTests` calls every listed tool without a token and fails if one answers that
  shouldn't. `McpToolHelpers.RequireUser` stays as the backstop inside each tool.
- **The whole toolset is listed to everyone**, each account tool's description prefixed `[Authorized]`. A tool
  the model cannot see is a tool it cannot offer, so hiding them left it unable to tell the user what the site
  can do. The SDK's `AddAuthorizationFilters()` would honor `[Authorize]`/`[AllowAnonymous]` attributes, but
  it filters `tools/list` by them with no opt-out — and hard-throws if the attributes are present without it.
  Listing everything therefore means not using it, and not carrying the attributes at all.
- **A refused call is not an incident.** `McpToolErrors` is this host's `UseExceptionHandler`: a call-tool
  filter that turns the `McpException`s the tools raise on purpose into the `isError` result the model reads.
  The SDK does the same thing on its own — but only after logging it at Error as an unhandled exception, which
  alerts on every refused call. Catching it first, inside the SDK's outer handler, means nothing is logged at
  all, so no observability filtering is needed anywhere. Anything that isn't an `McpException` still reports.
- **An invalid or expired token is challenged, not downgraded** — including on the public tools, because the
  spec says an invalid or expired token MUST be answered with a 401. Stock ASP.NET would fail open here
  (authentication that fails leaves an anonymous principal, and an endpoint with no authorization requirement
  never challenges), which silently demoted a stale token to anonymous instead of letting the client refresh
  it. A caller that presents no token at all is still served the public tier.
- `McpAuth` wires JWT bearer validation (Supabase issuer + JWKS) as the *authenticate* scheme and the MCP SDK's
  scheme as the *challenge* scheme. The SDK's `AddMcp` handler serves `/.well-known/oauth-protected-resource`
  from the configured `ProtectedResourceMetadata`.
- **The consent screen is ours.** Supabase delegates it: it redirects to `/oauth/consent` on the frontend,
  which reads the request with `supabase.auth.oauth.getAuthorizationDetails` and approves or denies it as the
  signed-in user. See `frontend/src/pages/oauth/consent.astro`.
- **Audience binding is not enforced yet.** `ValidateAudience = false` (documented in `McpAuth.cs`): the `aud`
  Supabase mints for third-party tokens can't be confirmed until the OAuth server is enabled, so the host
  validates issuer + signature + lifetime for now and should tighten to the RFC 8707 resource audience after.

Connecting is just the URL — the assistant does the rest:

```bash
claude mcp add --transport http kalandra https://mcp.kalandra.tech/mcp
```

## Tools

| Tool | Access | Handler / source |
|------|--------|------------------|
| `submit_job_offer` | Account | `CreateJobOfferHandler.CreateAndSave` |
| `list_my_job_offers` | Account | `ListJobOffersHandler.List` |
| `get_job_offer_comments` | Account | `ListCommentsHandler.List` |
| `add_job_offer_comment` | Account | `AddCommentHandler.AddAndSave` |
| `list_blog_posts` | Public | `BlogFeedClient` (site RSS feed) + `GetBlogPostStatsHandler` |
| `get_blog_post_comments` | Public | `GetBlogCommentsHandler.GetForDisplay` |
| `post_blog_comment` | Account | `PostBlogCommentHandler.PostAndSave` |
| `get_my_comments` | Account | `ListMyBlogCommentsHandler` + `ListMyJobOfferCommentsHandler` |

Account tools act as the authenticated caller and open with `McpToolHelpers.RequireUser`; `ToolAuthorizationTests`
holds the list of the two public tools and refuses to let anything else answer an anonymous caller, so a new
tool is account-only unless it is deliberately added there. Tools return the same response contracts the
controllers serialize — no separate DTO layer. Domain errors become `McpException` messages phrased for a
language model to act on, the MCP equivalent of the controllers' RFC 7807 responses; `McpToolErrors` turns
them into `isError` results, so throwing one is this host's `return NotFound()`.

`list_blog_posts` links to the public post pages (there is no separate content tool — assistants fetch the
link). It serves everyone the same per-post totals the blog index shows — views, unique visitors, reactions,
comments — via the batch `GetBlogPostStatsHandler` the REST stats endpoint uses, and adds `viewerViews` and
`watched` only for a signed-in caller; for an anonymous one those fields stay null.

## Rate limiting

One `McpRateLimitPolicies.Mcp` sliding-window bucket for the whole endpoint (per signed-in user, per client IP
for anonymous callers) — generous, since one assistant session lists tools and makes several calls in quick
succession. No captcha: Turnstile is a browser concern.

## Configuration

| Key | Meaning | Local default |
|-----|---------|---------------|
| `Mcp:ResourceUri` | The host's own public URL — the OAuth resource id in the metadata | `http://localhost:5100` |
| `BlogFeed:RssUrl` | The blog's RSS feed, for `list_blog_posts` | `http://localhost:4321/rss.xml` |
| `Supabase:ProjectUrl` | Also the JWT issuer (`{url}/auth/v1`) and the advertised authorization server | `http://localhost:54321` |
| `Supabase:ServiceKey` | For `IUserInfoService` (comment authors) and `IStorageService` | local demo key |
| `ConnectionStrings:DefaultConnection` | The same database the API uses | local Postgres |

Production refuses to start with a localhost resource URI, feed URL, database, or project URL.

## Local development

`aspire run` starts the MCP host next to the API on the per-worktree Postgres, points `BlogFeed:RssUrl` at the
Astro dev server, and sets `Mcp:ResourceUri` to its own endpoint. Standalone: `dotnet run` in
`backend/src/Kalandra.McpServer` serves `/mcp` on `:5100`.

## Deployment

Its own image and blue/green Quadlet slots on ports **8082/8083** (the API's are 8080/8081), fronted by the
shared Caddy through its own `kalandra-mcp.caddy` fragment — a separate file from the API's, so each deploy
rewrites only its own port while the base config glob-imports both. The `mcp-deploy` job runs after
`backend-deploy` (the API applies the schema first) and gates promotion on `/health/live` reporting the
deployed commit, exactly like the API.

Deploying needs only the `mcp.kalandra.tech` DNS record and the shared Caddy cert covering the subdomain.

## Testing

- `Kalandra.Blog.Tests/BlogFeedParseTests` — pure unit tests for RSS parsing (the feed reader lives in
  `Kalandra.Blog/Feed/`).
- `Kalandra.McpServer.Tests` — HTTP-level tests against the real host with a stubbed RSS feed:
  `OAuthResourceServerTests` pins the protected-resource metadata document, `AnonymousAccessTests` pins the
  anonymous tier (whole toolset listed, public tools callable, an invalid token served as anonymous), and
  `ToolAuthorizationTests` sweeps every listed tool without a token, checking each one challenges or answers
  the way its description advertises — the gate owns a hand-kept list, so this is what stops a new tool from
  leaking. `McpToolErrorsTests` pins that a deliberate tool error is logged nowhere, since the response looks
  identical either way and the only symptom is Sentry filling up.
- The tools' behaviour is covered by the domain handlers' own tests, which they share with the controllers.
- The frontend consent screen is covered by `frontend/tests/pages.spec.ts` (missing request, sign-in prompt,
  noindex).
- The streamable-HTTP transport itself is standard SDK wiring and is left to an end-to-end smoke test.
