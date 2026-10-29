# Notifications

## Purpose

Job-offer and blog activity emails the people involved
([requirements](../product-requirements.md#job-offers)): a stored offer or comment must always get
its emails, and no email may go out twice.

## How it works

Emails are a side effect of committed events, delivered by Marten event subscriptions running
under the API's async daemon. A command handler appends its event like any other write and the
HTTP response returns as soon as it lands; the subscription then reacts to the stored event and
sends the emails.

- `JobOfferNotificationSubscription` (`Kalandra.JobOffers/Notifications/`) emails the owner about
  new offers, and the owner and the offer's author about comments.
- `BlogCommentNotificationSubscription` (`Kalandra.Blog/Notifications/`) emails the blog author
  about every comment, and the parent comment's author about replies.
- Deciding who gets which email is pure code next to each subscription, unit-tested without a
  database.
- Mail goes out through `IEmailSender` (`Kalandra.Infrastructure/Email/`) over SMTP: the Brevo
  relay in production, the Supabase CLI's Mailpit locally.

## Decisions

- **Marten subscriptions, not a workflow engine** — the daemon already delivers each committed
  event at least once and tracks its own progress in the same PostgreSQL, so durable delivery
  needs no extra service to host, secure, and monitor. Not a workflow engine such as Temporal,
  which adds a server, its own databases, and a dashboard to run for a handful of emails.
- **Store first, notify after** — the write never waits for SMTP, so a mail outage can't fail a
  submission or a comment.

## Invariants

- Each delivery stores a `*NotificationSent` marker in its own transaction right after the send.
  The daemon replays a whole page after any failure, and the marker is what keeps an
  already-delivered email from going out again.
- Subscriptions start with `SubscribeFromPresent`, so a fresh deploy never replays (and re-emails)
  history.
- The daemon runs in `HotCold` mode, so only one instance delivers while the blue and green slots
  overlap during a deploy.
- Only `Kalandra.Api` runs the daemon; the [MCP host](mcp-server.md) just appends events.
- Production refuses to start when `Email:Host` points at a local mail catcher, or when a real
  relay has no credentials.
