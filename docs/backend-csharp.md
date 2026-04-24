# C# Conventions

Language-level rules that apply across all backend projects.

## Parse, don't validate

Prefer strong types that make invalid states unrepresentable. Use the `Kalicz.StrongTypes` library (`NonEmptyString`, etc.) and BCL types over raw primitives:

- `NonEmptyString` instead of `string` for required text fields.
- `MailAddress` instead of `string` for emails.
- `Guid` (or a typed ID) instead of `string` for identifiers.
- `DateTimeOffset` instead of `string` for timestamps.

Parse at the boundary (API layer), then pass the strong type through commands and into the domain. The boundary is the DTO itself — required text fields are typed as `NonEmptyString` (or `NonEmptyString?` for PATCH "no change" semantics), so the controller can pass them straight through to the command:

```csharp
// Good — DTO holds NonEmptyString; controller has no boilerplate
public record CreateJobOfferRequest(
    NonEmptyString CompanyName,
    [EmailAddress] NonEmptyString ContactEmail,
    ...);

var command = new CreateJobOfferCommand(
    CompanyName: request.CompanyName,
    ContactEmail: request.ContactEmail,
    ...);

// Bad — raw string passed through, domain has to validate
var command = new CreateJobOfferCommand(
    CompanyName: request.CompanyName,  // might be empty
    ContactEmail: request.ContactEmail, // might not be an email
    ...);
```

The `StrongTypes` `[JsonConverter]` rejects empty JSON strings as `JsonException`, which ASP.NET surfaces as an RFC 7807 400. For `[FromForm]` / query string inputs — where model binding doesn't go through the JSON converter — the `NonEmptyStringModelBinderProvider` in `Kalandra.Api/Infrastructure/` fills the same role and is registered in `Program.cs` via `ModelBinderProviders.Insert(0, ...)`.

Most `System.ComponentModel.DataAnnotations` attributes don't recognise strong types — their `IsValid` checks `value is string` and returns `false` for anything else. `[MaxLength]` even throws `InvalidCastException`. Keep these attributes on plain `string?` / `string` properties only, and rely on the type itself for invariants on strong-typed properties.

`NonEmptyString` has an implicit conversion to `string`, so a domain record holding one deserializes to (and serializes from) the same JSON string and passes anywhere a `string` is accepted. Reach for `.ToNonEmpty()` / `.AsNonEmpty()` only when crossing from a loose primitive string you don't control (e.g. `IFormFile.FileName`) into domain code.

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

## `Result<TSuccess, TError>` type

`Result<TSuccess, TError>` is a discriminated-union-like result type from the `Kalicz.StrongTypes` package (available via `global using StrongTypes;`). Use it for handler and entity methods whose "failure" is a business decision, not an exception.

- Return the success value directly — the implicit operator wraps it: `return newId;` or `return offer;`.
- Return the error value directly — the implicit operator wraps it: `return EditJobOfferError.NotAuthorized;`.
- Unwrap via pattern matching: `if (result.Error is { } error) return Map(error);` then read success as `result.Success!` (`!.Value` for value-type payloads).
- `result.IsSuccess` / `result.IsError` are available when you don't need the payload.

Exceptions are reserved for truly exceptional conditions (storage failures, DB outages, concurrency conflicts). A user trying to edit someone else's job offer is not an exception — it's a domain error carried by the `Result`.

## Enum switches: no default branch

Never use `default` or `_` in switch expressions on enums. Enumerate every case explicitly so the compiler warns when a new value is added. This is critical for the two-enum error contract — a new domain error that isn't mapped must be a compile-time signal, not a silent fallthrough.

## Keep method calls compact

Prefer single-line calls. When one argument is too large for a one-liner, extract it into a local variable — don't break the call itself across lines.

```csharp
// Good — complex argument extracted, call stays on one line
var fileOptions = new FileOptions { ContentType = file.ContentType };
await bucket.Upload(ms.ToArray(), storagePath, fileOptions);

// Bad — call broken across many lines
await bucket.Upload(
    ms.ToArray(),
    storagePath,
    new FileOptions { ContentType = file.ContentType });
```

For logging, keep it on one line. If parameters make the line too long, split into the message line and the parameters line — but never more than two lines. Short arguments like an exception variable belong on the first line before the message string.

```csharp
// Good — single line
logger.LogError(ex, "Failed to upload {StoragePath} to Supabase Storage", storagePath);

// Good — message + parameters when the line would be very long
logger.LogError(ex, "Failed to upload {StoragePath} to bucket {Bucket}",
    storagePath, bucketName);

// Bad — broken across too many lines
logger.LogError(
    ex,
    "Failed to upload {StoragePath} to Supabase Storage",
    storagePath);
```
