# MCP Server

The API exposes a [Model Context Protocol](https://modelcontextprotocol.io) endpoint at **`/mcp`** so AI
assistants (Claude, IDE agents, …) can interact with kalandra.tech: submit and follow up on job offers,
browse blog posts, and read or write comments. It is served by the same `Kalandra.Api` host as the REST API
(streamable HTTP, stateless), reachable in production at `https://api.kalandra.tech/mcp`.

## Architecture: a second front door, not a second service

The MCP tools live in `Kalandra.Api/Features/Mcp/` and are thin adapters over the **same domain handlers the
controllers call**:

```
REST controllers ─┐
                  ├─►  domain handlers  ─►  Marten (events)  ─►  notification subscriptions (email)
/mcp tools ───────┘    (CreateJobOffer, PostBlogComment, …)
```

- **Same logic as the UI.** A tool builds the same command/query record a controller builds and calls the
  same handler, as the authenticated user. Validation, the event store, and the Marten notification
  subscriptions all behave identically — there is no second write path.
- **In-process.** Because the tools run in the API host, they share its DI container, auth pipeline, database
  session, and the notification subscriptions. Appending an event from a tool triggers the same email
  notification a controller write would.
- **Blog posts come from the RSS feed** (`BlogFeedClient`), because the backend's post catalog holds only
  slugs and stream ids — the frontend owns post titles and summaries.

## Authentication

The MCP endpoint shares the API's JWT bearer pipeline. A tool that acts as a user requires the connection to
send the user's **Supabase access token**:

```
Authorization: Bearer <supabase access token>
```

`UseAuthentication()` validates the token and populates `HttpContext.User` for `/mcp` requests just like any
controller request, so `ICurrentUserAccessor` gives the tools the same `CurrentUser` the controllers get. The
server never owns credentials. Reading blog posts and their comments needs no token; the write/account tools
throw a clear "connect with an Authorization header" error when none is present. Example client config:

```bash
claude mcp add --transport http kalandra https://api.kalandra.tech/mcp --header "Authorization: Bearer <token>"
```

## Tools

| Tool | Auth | Handler / source |
|------|------|------------------|
| `submit_job_offer` | ✅ | `CreateJobOfferHandler.CreateAndSave` |
| `list_my_job_offers` | ✅ | `ListJobOffersHandler.List` |
| `get_job_offer_comments` | ✅ | `ListCommentsHandler.List` |
| `add_job_offer_comment` | ✅ | `AddCommentHandler.AddAndSave` |
| `list_blog_posts` | — | `BlogFeedClient` (site RSS feed) + `GetViewerBlogViewsHandler` |
| `get_blog_post_comments` | — | `GetBlogCommentsHandler.GetForDisplay` |
| `post_blog_comment` | ✅ | `PostBlogCommentHandler.PostAndSave` |
| `get_my_comments` | ✅ | `ListMyBlogCommentsHandler` + `ListMyJobOfferCommentsHandler` |

Tools return the same response contracts the controllers serialize (`GetJobOfferDetailResponse`,
`CommentResponse`, `BlogCommentResponse`, …) — no separate DTO layer. Domain errors are translated into
`McpException` messages phrased for a language model to act on, the MCP equivalent of the controllers' RFC
7807 responses.

`get_my_comments` aggregates the caller's comments across blog posts and job offers together with the replies
they received.

`list_blog_posts` is public, but when the caller is signed in it enriches each post with `viewerViews` (their
own view count) and `watched` (whether they've read it) — one lean query over the view documents
(`GetViewerBlogViewsHandler`), not the heavier per-post totals `GetBlogPostStatsHandler` computes for the blog
index. Anonymous callers get both fields as `null`.

## Rate limiting

The whole `/mcp` endpoint carries one `RateLimitPolicies.Mcp` sliding-window bucket (per signed-in user, or
per IP for anonymous reads) — generous, since one assistant session lists tools and makes several calls in
quick succession. There is no captcha: Turnstile is a browser concern, and the domain handlers a tool calls
don't involve it.

## Configuration

| Key | Meaning | Local default |
|-----|---------|---------------|
| `BlogFeed:RssUrl` | The blog's RSS feed, for `list_blog_posts` | `http://localhost:4321/rss.xml` |

Production refuses to start with a localhost feed URL, mirroring the other config guards. Everything else the
MCP tools need (database, auth, handlers) is the API's existing configuration.

## Local development

`npm run aspire` runs the API with the `/mcp` endpoint; the AppHost points `BlogFeed:RssUrl` at the Astro dev
server. The MCP endpoint is at `http://localhost:<api-port>/mcp`. Standalone: `dotnet run` in
`backend/src/Kalandra.Api` serves `/mcp` on the same port as the REST API.

## Deployment

Nothing MCP-specific: the endpoint ships inside the API image and the blue/green API deploy. It is reached
through the existing `api.kalandra.tech` Caddy route at `/mcp`; the only added config is `BlogFeed__RssUrl`
in the API's environment.

## Testing

- `Kalandra.Blog.Tests/BlogFeedParseTests` — pure unit tests for RSS parsing (the feed reader lives in `Kalandra.Blog/Feed/`).
- `Features/Mcp/McpToolsTests` — drives the tools directly against the real domain handlers and database
  (`TestWebApplicationFactory`, real Postgres) with a chosen caller: the auth guard, command building, error
  translation, ownership boundaries, and the signed-in read-status enrichment. The factory serves the
  blog-feed tool a canned RSS document. (The streamable-HTTP transport itself is standard SDK wiring and can't
  run over the in-memory test server, so it's left to an end-to-end smoke test.)
