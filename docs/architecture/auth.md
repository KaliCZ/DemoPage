# Auth

## Purpose

Users sign in with email and password or Google, recover a forgotten password, and act on their
own job offers and comments; the admin sees and moderates everything; AI assistants act for a
user without seeing their credentials
([requirements](../product-requirements.md#accounts)).

## How it works

Supabase Auth owns identities, sessions, and tokens. The browser talks to it directly through
`@supabase/supabase-js`; the backend only validates the tokens it issues.

- **Frontend** — `Layout.astro` creates the Supabase client and exposes `window.__supabase`,
  `window.__getAccessToken()`, `window.__getUser()`, `window.__openAuthDialog()`, and an
  `auth-change` window event; module code uses the typed wrappers in `lib/auth.ts`. The sign-in
  dialog is part of the layout, so pages never add it.
- **Password reset** — the dialog's reset mode emails a Supabase recovery link. Like OAuth, the
  link lands on `/auth/callback`, the only page that accepts session tokens in the URL, which
  forwards to `/reset-password`. That page offers the new-password form only to sessions opened
  from an emailed link (`amr` of `otp`, `magiclink`, or `recovery`) — the sessions Supabase lets
  set a password without the current one. Everyone else changes it on the profile page.
- **Backend validation** — both hosts validate the bearer JWT against Supabase's OpenID Connect
  metadata (`{projectUrl}/auth/v1/.well-known/openid-configuration`) with
  `RefreshOnIssuerKeyNotFound`, so rotated signing keys are picked up without a restart.
- **Roles** — `OnTokenValidated` projects the token's `app_metadata.roles` array into role claims,
  keeping only names that parse into the `UserRole` enum. The admin role is granted by writing
  `app_metadata` in Supabase ([deployment.md](deployment.md#supabase-project)).
- **Current user** — controllers and tools read the caller through `ICurrentUserAccessor`, never
  from `HttpContext.User`.
- **Account linking** — adding a password to a Google account goes through
  `SupabaseAdminService`, which calls Supabase's admin API with the service key.
- **AI assistants** — Supabase's OAuth 2.1 server issues tokens for MCP clients; the consent
  screen is the site's own `/oauth/consent` page. See [mcp-server.md](mcp-server.md).
- **Bot protection** — Cloudflare Turnstile guards sign-in, sign-up, password reset, and job-offer
  submission. Writes are rate-limited per user, never per IP; after a rate-limit response the
  client may pass an interactive Turnstile challenge and retry
  ([conventions/api.md](../conventions/api.md#rate-limiting)).

## Decisions

- **Supabase Auth** — it comes with the Supabase database already in use, runs locally through the
  Supabase CLI, and can act as the OAuth authorization server that MCP clients need. Not a custom
  implementation, because OAuth flows and token security are costly to get right; not
  WorkOS, Clerk, or Auth0, which add another paid dependency and don't all run locally.
- **Roles in `app_metadata`** — only the service key can write it, so a user can't grant
  themselves a role, and the role travels inside the token with no lookup per request.

## Invariants

- Every API controller is `[Authorize]` at class level; anonymous endpoints opt out explicitly.
- An unknown role string in a token grants nothing.
- Session tokens in a URL are accepted only on `/auth/callback`.
