# MCP Server

## Purpose

Let AI assistants use the site over the [Model Context Protocol](https://modelcontextprotocol.io):
browse blog posts and comments without an account, and — signed in as a user — submit and follow
up on job offers and write comments, under the same rules as the website
([requirements](../product-requirements.md#ai-assistants)).

## How it works

`Kalandra.McpServer` serves **`https://mcp.kalandra.tech/mcp`** over stateless streamable HTTP.
Connecting needs only the URL; the assistant handles sign-in itself:

```bash
claude mcp add --transport http kalandra https://mcp.kalandra.tech/mcp
```

- **Same domain as the UI** — a tool builds the same command or query record a controller builds
  and calls the same handler, so validation and the event store behave identically. Tools return
  the same response contracts the controllers serialize. See [backend.md](backend.md#two-hosts-one-domain).
- **Tools** live in `Kalandra.McpServer/Tools/`. Two are public — `list_blog_posts` and
  `get_blog_post_comments`; every other tool requires an account and is listed with an
  `[Authorized]` prefix on its description.
- **Blog posts come from the site's RSS feed**, because the backend's post catalog holds only slugs
  and stream ids — the frontend owns titles and summaries. `list_blog_posts` links to the public
  post pages and adds the per-post totals the blog index shows, plus the caller's own view count
  and watch flag when signed in.
- **Domain errors become `ToolRefusalException`s** with messages phrased for a language model —
  this host's equivalent of the controllers' RFC 7807 responses. `McpToolErrors` turns them into
  the `isError` result the model reads.
- **Rate limiting** — one sliding-window bucket for the whole endpoint, per signed-in user or per
  client IP for anonymous callers, generous because one assistant session lists tools and makes
  several calls in a row. No CAPTCHA: Turnstile is a browser concern.

### Authentication

The host is an OAuth 2.0 resource server; Supabase's OAuth 2.1 server is the authorization server
and runs the whole flow. The host issues no tokens and holds no credentials.

```
Assistant ──1── POST /mcp (no token)  →  anonymous tier: whole toolset listed, public tools callable
          ──2── tools/call on an [Authorized] tool  →  401 + WWW-Authenticate: Bearer resource_metadata=…, scope=…
          ──3── GET /.well-known/oauth-protected-resource  →  { resource, authorization_servers: [<supabase>/auth/v1], scopes_supported }
          ──4── OAuth 2.1 + PKCE against Supabase (discovery, client registration, consent)
          ──5── POST /mcp with the access token  →  the full toolset, acting as that user
```

- `McpAuth` validates the bearer JWT (Supabase issuer and JWKS) as the *authenticate* scheme and
  uses the MCP SDK's scheme to *challenge*; the SDK serves the protected-resource metadata.
- `McpAccountGate`, middleware in front of the endpoint, issues the challenge for account tools.
- Supabase delegates the consent screen to the site: `/oauth/consent`
  (`frontend/src/pages/oauth/consent.astro`) reads the request with
  `supabase.auth.oauth.getAuthorizationDetails` and approves or denies it as the signed-in user.

## Decisions

- **A separate host, not a route on the API** — the two authenticate differently: the API takes a
  bearer token from the site's own frontend, the MCP host is a resource server that third-party
  assistants discover and sign in to.
- **Challenge, don't refuse** — the [authorization spec](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
  puts authorization at the transport level: a 401 with `WWW-Authenticate` is the only refusal a
  client's OAuth code acts on. A JSON-RPC error or a tool error saying "please sign in" reaches only
  the model, which can only relay it. The challenge also names the `scope` to request.
- **The gate is middleware** — inside a tool the response is already a JSON-RPC envelope and can't
  become a 401, and stock `RequireAuthorization` would shut the anonymous tier out of the whole
  endpoint without ever seeing which tool the body names.
- **The whole toolset is listed to everyone** — a tool the model can't see is a tool it can't
  offer, so hiding account tools would leave it unable to tell the user what the site can do. That rules
  out the SDK's `AddAuthorizationFilters()`, which filters `tools/list` by `[Authorize]` with no
  opt-out and throws if the attributes are present without it — so the tools carry no auth
  attributes at all.
- **Refusals are caught before the SDK logs them** — the SDK turns a tool exception into an
  `isError` result too, but only after logging it at Error, which would alert on every refused
  call. `McpToolErrors` catches only this host's own `ToolRefusalException`, so anything else —
  the SDK's `McpException`s included — still reports.
- **An invalid or expired token is challenged, not downgraded** — the spec requires a 401 even on
  public tools. Stock ASP.NET fails open here (a failed authentication leaves an anonymous
  principal), which would turn a stale token into an anonymous session instead of letting the
  client refresh. That challenge carries `error="invalid_token"` (RFC 6750), the signal to refresh
  rather than start a new sign-in; a caller that sends no token on a public tool is still served.

## Invariants

- The gate fails closed: an anonymous `tools/call` is challenged unless it names a public tool —
  which covers unknown tools, new tools, and malformed calls from scanners.
  `ToolAuthorizationTests` calls every listed tool without a token, so a new tool is account-only
  unless it is deliberately made public.
- Every account tool also starts with `McpToolHelpers.RequireUser`, as a backstop behind the gate.
- The host never runs the async daemon or the notification subscriptions, and runs
  `AutoCreate.None` in production — the API owns both ([notifications.md](notifications.md)).
- Production refuses to start with a localhost resource URI, feed URL, database, or Supabase URL;
  local defaults are in the host's `appsettings.json`, and the AppHost points the feed and resource
  URI at the local services.

## Deployment

Its own image and blue/green slots on ports 8082/8083, routed by its own Caddy fragment
`kalandra-mcp.caddy`. `mcp-deploy` runs after `backend-deploy` and promotes a slot only once
`/health/live` reports the deployed commit ([deployment.md](deployment.md)).

## Deferred

- **Audience binding** — `ValidateAudience = false`: the `aud` Supabase mints for third-party
  tokens isn't confirmed yet, so the host checks issuer, signature, and lifetime. Tighten to the
  RFC 8707 resource audience once it is.
