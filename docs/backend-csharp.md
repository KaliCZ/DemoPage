# C# Conventions

Language-level rules that apply across all backend projects.

## Parse, don't validate

Prefer strong types that make invalid states unrepresentable. Use the `Kalicz.StrongTypes` library (`NonEmptyString`, etc.) and BCL types over raw primitives:

- `NonEmptyString` instead of `string` for required text fields.
- `MailAddress` instead of `string` for emails.
- `Guid` (or a typed ID) instead of `string` for identifiers.
- `DateTimeOffset` instead of `string` for timestamps.

Parse at the boundary (API layer), then pass the strong type through commands and into the domain. If parsing fails, the error is caught at the edge — the domain never has to check for empty strings or malformed emails.

```csharp
// Good — parsed at the API boundary, domain receives NonEmptyString
var command = new CreateJobOfferCommand(
    CompanyName: request.CompanyName.AsNonEmpty().Get(),
    ContactEmail: new MailAddress(request.ContactEmail),
    ...);

// Bad — raw string passed through, domain has to validate
var command = new CreateJobOfferCommand(
    CompanyName: request.CompanyName,  // might be empty
    ContactEmail: request.ContactEmail, // might not be an email
    ...);
```

## Named arguments

1. **Multi-line calls**: every argument gets a named parameter.
2. **Opaque literals**: `null`, `true`, `false`, `0`, `""`, `[]` get named parameters. If the meaning is obvious from the variable name, the name can be omitted on single-line calls.

```csharp
// Good — multi-line, all named
var (success, error, edited) = offer.Edit(
    userId: userId,
    userEmail: userEmail,
    companyName: request.CompanyName,
    timestamp: timeProvider.GetUtcNow());

// Good — single line, null is labeled
var result = await listHandler.HandleAsync(userId: null, page, pageSize, ct);
```

## Enum switches: no default branch

Never use `default` or `_` in switch expressions on enums. Enumerate every case explicitly so the compiler warns when a new value is added. This is critical for the two-enum error contract — a new domain error that isn't mapped must be a compile-time signal, not a silent fallthrough.
