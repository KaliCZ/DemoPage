# Job Offers

## Purpose

The hire-me flow: a signed-in user submits a job offer, follows its status, and talks to the
owner through comments; the admin reviews every offer
([requirements](../product-requirements.md#job-offers)).

## How it works

`JobOffer` (`Kalandra.JobOffers`) is an event-sourced aggregate. Submitting starts its stream;
edits, cancellation, and status changes append to it; comments go to a separate comment stream
derived from the offer's id ([persistence.md](persistence.md)).

- **State machine** — `JobOffer.ChangeStatus` owns the legal transitions; handlers never decide
  them. The submitter edits only while Submitted and cancels only while Submitted or In review.
- **History** — the offer's history page reads its event stream; each event carries the acting
  user and timestamp, so the audit trail needs no separate table.
- **Attachments** — the handler uploads them to Supabase Storage under
  `{userId}/{offerId}/` and records them on the submission event. Downloads stream through the API
  after the same access check as the offer itself, so the bucket stays private.
- **Retry-safe submission** — the client generates the offer id. Resubmitting the same id as the
  same user returns the existing offer instead of creating a second one; the same id from another
  user is refused.
- **Front doors** — the website calls `JobOffersController` after a Turnstile check and a per-user
  rate limit; AI assistants call the same handlers through MCP tools.
- New offers and comments trigger [notification emails](notifications.md).

## Decisions

- **Event sourcing for offers** — the auditable history is a product requirement, and the event
  stream provides it by construction instead of as a second write that could be forgotten.
- **Comments on their own stream** — they are high-volume and don't change the offer's state, so
  keeping them off the main stream keeps snapshot rebuilds short.

## Invariants

- A query for an offer the caller doesn't own and can't administer returns `null`, which the API
  maps to `404` — never `403`, so non-owners can't tell "not yours" from "doesn't exist".
- Every state change is an event that names the acting user.
- Attachment limits (count, total size, content types) are enforced by the handler, whatever the
  front door already checked.
