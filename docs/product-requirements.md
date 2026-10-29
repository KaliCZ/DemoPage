# Product Requirements

> What kalandra.tech must do, in product terms. No technology here — the
> [architecture docs](architecture/README.md) cover how.

## Purpose

kalandra.tech presents Pavel Kalandra to potential employers and recruiters, and lets them
approach him with a job offer. The site is also built in public as a showcase: it documents its
own architecture, decisions, and progress, and serves as a real, deployed playground for new
technologies.

## Users and roles

- **Visitor** — anyone without an account: reads the pages and the blog, reacts to posts.
- **Signed-in user** — submits and follows up on job offers, comments on blog posts, manages
  their profile.
- **Admin** — the site owner: reviews every job offer, moves it through its statuses, and
  moderates blog comments.
- **AI assistant** — a third-party assistant acting for a visitor or, after the user signs in
  through it, as that user.

## Requirements

Invariants that must hold absolutely are marked **(invariant)**; everything else is expected
behavior.

### Site and content

- Every page is available in English and Czech, and in a light and a dark theme that follows the
  system preference until the visitor picks one.
- The Project page shows how the site is built: goals, architecture diagram, key decisions,
  roadmap, and lessons learned.
- The Projects page lists Pavel's projects; the Strong Types page documents his open-source
  library.
- Public pages are indexable by search engines; account and admin pages are never indexed.

### Accounts

- A user signs up and signs in with email and password, or with Google.
- A user who forgot their password can reset it through an emailed link.
- A user who signed up with Google can add a password to the same account, and can upload an
  avatar.

### Job offers

- A signed-in user submits a job offer: company, contact, role, description, salary range,
  location, remote flag, notes, and up to 5 attachments of up to 15 MB in total, from an allowed
  set of document and image types.
- Submission is protected against bots, and rate-limited per user.
- A user sees only their own offers; the admin sees all of them. A user can't learn that someone
  else's offer exists. **(invariant)**
- The submitter can edit an offer while it is still Submitted, and cancel it while it is Submitted
  or In review.
- The admin moves an offer from Submitted to In review, Let's talk, or Declined, and from In
  review to Let's talk or Declined. Let's talk, Declined, and Cancelled are final.
- Every change to an offer is kept in a history that shows who changed what, and when.
  **(invariant)**
- The submitter and the admin can comment on an offer; both see every comment.
- The admin is emailed about every new offer and comment; the submitter is emailed when someone
  else comments on their offer. A stored offer or comment always gets its emails, and an email is
  not sent twice. **(invariant)**

### Blog

- A post is written in English, Czech, or both. A post appears in a language only when its title,
  summary, and body are all written in that language.
- One RSS feed covers the whole blog.
- Anyone can react to a post with one or more of five reactions, and see the totals.
- Each post shows its views and unique visitors. A visitor's anonymous views and reactions move to
  their account when they sign in.
- Signed-in users comment on posts and reply to comments. A comment's author or the admin can
  delete it; nobody can reply to a deleted comment.
- The blog author is emailed about every comment, and a comment's author is emailed about replies
  to it.

### AI assistants

- An assistant can list blog posts and read their comments without an account.
- To act as a user, the assistant signs the user in through the site's own consent screen; it
  never sees the user's credentials. **(invariant)**
- Through an assistant, a user can submit and follow up on job offers, post blog comments, and see
  their own comments with the replies they received — under exactly the same rules as on the
  website. **(invariant)**

## Non-goals

- A content-management UI — blog posts are written and reviewed in the repository.
- Multiple blog authors or public user profiles.

## Glossary

- **Job offer** — a hiring proposal a user submits to Pavel. Its statuses are Submitted, In review,
  Let's talk, Declined, and Cancelled.
- **Visitor id** — the anonymous identity a browser keeps so that views and reactions count before
  sign-in.
- **Reaction** — one of five emoji-style responses to a blog post: thumbs up, thumbs down, heart,
  insightful, rocket.
- **Variant** — one language version of a blog post.

## Open questions

- The roadmap's remaining items — Slack notifications, emails on status changes, product
  analytics, and paid job-offer tiers — are they still planned?
