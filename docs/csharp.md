# C# Conventions

Language-level rules that apply across all backend projects. These are about *how we write C#*, not about architecture — see `docs/architecture.md` and the per-layer guides for that.

## Table of contents

- [Named arguments](#named-arguments)
- [Enum switches: no default branch](#enum-switches-no-default-branch)

## Named arguments

Two rules for method/constructor calls:

1. **Multi-line calls**: When a call spans multiple lines, every argument gets a named parameter.
2. **Opaque literal values**: When passing `null`, `true`, `false`, `0`, `""`, `[]`, or similar literals where the meaning isn't obvious from context, use named parameters. If the meaning is obvious from the variable name (e.g., `userId`, `request`), the name can be omitted on single-line calls.

```csharp
// Good — multi-line, all named
var (success, error, edited) = offer.Edit(
    userId: userId,
    userEmail: userEmail,
    companyName: request.CompanyName,
    timestamp: timeProvider.GetUtcNow());

// Good — single line, null is labeled
var result = await listHandler.HandleAsync(userId: null, page, pageSize, ct);

// Bad — multi-line without names
var (success, error, edited) = offer.Edit(
    userId,
    userEmail,
    request.CompanyName,
    timeProvider.GetUtcNow());

// Bad — null without label
var result = await listHandler.HandleAsync(null, page, pageSize, ct);
```

## Enum switches: no default branch

Never use `default` or `_` catch-all branches in switch expressions or statements that switch on an enum value. Always enumerate every case explicitly.

**Why:** When a new value is added to the enum, the compiler warns about unhandled cases. A `default` branch silently swallows new values and hides the fact that the switch needs updating. This is especially critical for the two-enum error contract (see `docs/api.md`) — a new domain error that isn't mapped to an API error must be a compile-time signal, not a silent runtime fallthrough.

```csharp
// Good — compiler warns when a new status is added
return status switch
{
    JobOfferStatus.Submitted => "Submitted",
    JobOfferStatus.InReview  => "In Review",
    JobOfferStatus.LetsTalk  => "Let's Talk",
    JobOfferStatus.Declined  => "Declined",
    JobOfferStatus.Cancelled => "Cancelled",
};

// Bad — new enum values silently fall through
return status switch
{
    JobOfferStatus.Submitted => "Submitted",
    JobOfferStatus.InReview  => "In Review",
    _ => "Unknown",
};
```

If you encounter an existing `default`/`_` on an enum switch, replace it with explicit cases.
