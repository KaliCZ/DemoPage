# MCP Server

`Kalandra.McpServer` is a standalone host that serves a [Model Context Protocol](https://modelcontextprotocol.io)
endpoint at **`https://mcp.kalandra.tech/mcp`** (streamable HTTP, stateless), so AI assistants can act on
kalandra.tech as the signed-in user: submit and follow up on job offers, browse blog posts, and read or write
comments.

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
Assistant ──1── POST /mcp (no token)
          ◄─2── 401 + WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"
          ──3── GET that metadata  →  { resource, authorization_servers: [<supabase>/auth/v1], scopes_supported }
          ──4── OAuth 2.1 + PKCE against Supabase (discovery, client registration, consent)
          ──5── POST /mcp with the access token  →  tools run as that user
```

- `McpAuth` wires JWT bearer validation (Supabase issuer + JWKS) as the *authenticate* scheme and the MCP SDK's
  scheme as the *challenge* scheme, so an unauthenticated call produces the 401 above. The SDK's `AddMcp`
  handler serves `/.well-known/oauth-protected-resource` from the configured `ProtectedResourceMetadata`.
- **The whole `/mcp` endpoint requires authorization.** Every tool acts as the signed-in user — there is no
  anonymous read tier, which is what makes an assistant discover Supabase and sign the user in on connect.
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

| Tool | Handler / source |
|------|------------------|
| `submit_job_offer` | `CreateJobOfferHandler.CreateAndSave` |
| `list_my_job_offers` | `ListJobOffersHandler.List` |
| `get_job_offer_comments` | `ListCommentsHandler.List` |
| `add_job_offer_comment` | `AddCommentHandler.AddAndSave` |
| `list_blog_posts` | `BlogFeedClient` (site RSS feed) + `GetViewerBlogViewsHandler` |
| `get_blog_post_comments` | `GetBlogCommentsHandler.GetForDisplay` |
| `post_blog_comment` | `PostBlogCommentHandler.PostAndSave` |
| `get_my_comments` | `ListMyBlogCommentsHandler` + `ListMyJobOfferCommentsHandler` |

All of them act as the authenticated caller. Tools return the same response contracts the controllers
serialize — no separate DTO layer. Domain errors become `McpException` messages phrased for a language model
to act on, the MCP equivalent of the controllers' RFC 7807 responses.

`list_blog_posts` enriches each post with `viewerViews` and `watched` from one lean query over the view
documents (`GetViewerBlogViewsHandler`), not the heavier per-post totals the blog index computes.

## Rate limiting

One `McpRateLimitPolicies.Mcp` sliding-window bucket for the whole endpoint (per signed-in user) — generous,
since one assistant session lists tools and makes several calls in quick succession. No captcha: Turnstile is a
browser concern.

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
- The tools' behaviour is covered by the domain handlers' own tests, which they share with the controllers.
- The frontend consent screen is covered by `frontend/tests/pages.spec.ts` (missing request, sign-in prompt,
  noindex).
- The streamable-HTTP transport itself is standard SDK wiring and is left to an end-to-end smoke test.
