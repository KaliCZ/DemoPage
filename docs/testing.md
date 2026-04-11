# Backend Testing Guide

xUnit v3 with Microsoft.Testing.Platform. Integration tests run against a real PostgreSQL via Testcontainers — no mocks for the database.

## Table of contents

- [Test infrastructure](#test-infrastructure)
- [The contract-detection rule](#the-contract-detection-rule)
- [API integration tests](#api-integration-tests)
- [Domain aggregate tests](#domain-aggregate-tests)
- [Concurrency tests](#concurrency-tests)
- [Test helpers](#test-helpers)
- [Adding a new test](#adding-a-new-test)

## Test infrastructure

`TestWebApplicationFactory` is the single test host. It:

- Spins up a disposable PostgreSQL container via Testcontainers (real database, not mocks).
- Replaces external HTTP dependencies with in-memory fakes: `InMemoryStorageService`, `AlwaysPassTurnstileValidator`, `FakeSupabaseAdminService`, `NoOpUserInfoService`.
- Configures JWT validation against `FakeJwksHandler` so tests can mint their own tokens via `JwtTestHelper.GenerateToken(userId, email, isAdmin)`.
- Bypasses the rate limiter via the `X-Interactive-Captcha` header.

Tests that need the full HTTP pipeline (controllers, auth, middleware) inject `TestWebApplicationFactory` as an `IClassFixture`. Tests that only exercise domain logic (aggregate decide/apply) don't need it at all.

## The contract-detection rule

**This is the single most important testing rule in this project.**

API integration tests must detect breaking contract changes at the test boundary. This means:

### Requests: use anonymous objects, not contract classes

```csharp
// Good — a renamed DTO property or changed enum value breaks the test
var response = await client.PutAsJsonAsync($"/api/job-offers/{id}", new
{
    companyName = "Updated Corp",
    contactName = "Jane Doe",
    contactEmail = "jane@updated.com",
    jobTitle = "CTO",
    description = "Updated description.",
    salaryRange = "$200k",
    location = "Remote",
    isRemote = true,
    additionalNotes = (string?)null,
}, ct);

// Bad — silently tracks renames, test keeps passing when the wire format changed
var response = await client.PutAsJsonAsync($"/api/job-offers/{id}",
    new EditJobOfferRequest { CompanyName = "Updated Corp", ... }, ct);
```

### Responses: assert on raw JSON, not deserialized DTOs

```csharp
// Good — uses JsonElement, asserts property names and string values
var json = await ParseJsonAsync(response);
Assert.Equal("Updated Corp", json.GetProperty("companyName").GetString());
Assert.Equal("Submitted", json.GetProperty("status").GetString());

// Bad — deserializes into the response class, silently tracks renames
var dto = await response.Content.ReadFromJsonAsync<JobOfferDetailResponse>();
Assert.Equal("Updated Corp", dto.CompanyName);
```

### Enums: assert on strings, not enum values

```csharp
// Good — catches if the enum member is renamed on the wire
Assert.Equal("Submitted", json.GetProperty("status").GetString());
Assert.Equal("InReview", updated.GetProperty("status").GetString());

// Bad — silently tracks enum renames
Assert.Equal(JobOfferStatus.Submitted.ToString(), ...);
```

### Error codes: assert on string literals

```csharp
// Good — catches if the API error enum member is renamed
AssertValidationError(json, "password", "PasswordTooShort");
AssertValidationError(json, "email", "AlreadyLinked");

// Bad — silently tracks API error enum renames
AssertValidationError(json, "password", nameof(LinkEmailError.PasswordTooShort));
```

### What IS allowed from domain code

Domain entity types, event types, and infrastructure types are fine for:
- **Test setup**: constructing entities, applying events to seed state.
- **DB seeding**: starting event streams with domain events via Marten sessions.
- **Direct DB assertions**: loading entities from the store to verify persistence.
- **Domain-only tests**: aggregate tests that exercise decide/apply directly (no HTTP involved).

The rule is about the **HTTP boundary** — what goes over the wire as JSON must be asserted with raw strings.

## API integration tests

Pattern: one test class per feature area, injecting `TestWebApplicationFactory`.

```csharp
public class JobOfferApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
```

**Conventions:**
- Use `TestContext.Current.CancellationToken` for cooperative cancellation — never `CancellationToken.None` in test methods (only in `ParseJsonAsync` helper where the stream must be fully consumed).
- Each test authenticates with a unique email to avoid cross-test interference — the factory shares a single database.
- Helper `CreateOfferAs(email)` creates a known-state offer and returns `(offerId, userId)` for follow-up assertions.
- Auth helpers: `Authenticate(email)` creates a fresh user ID and sets the bearer token. `AuthenticateAs(userId, email, isAdmin)` authenticates with a specific user ID.

**What to test:**
- Auth gates (401 for unauthenticated, 403 for wrong user/non-admin).
- Happy paths with full response shape assertion.
- Validation failures (400 with expected error codes).
- Authorization boundaries (owner vs. other user vs. admin).
- Side effects (history entries after a status change, comments visible to both parties).

## Domain aggregate tests

Pure unit tests that exercise the entity's decide/apply logic directly. No HTTP, no database, no `TestWebApplicationFactory`.

```csharp
public class JobOfferAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static JobOffer CreateSubmittedOffer(Guid? userId = null)
    {
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted(...));
        return offer;
    }

    [Fact]
    public void Edit_ByOwner_WhenSubmitted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.Edit(user: Owner, ...);

        Assert.True(result.IsSuccess);
        Assert.Equal("NewCo", result.Success.Get().CompanyName);
    }
}
```

**Conventions:**
- Use domain types directly (`CurrentUser`, `JobOffer`, event records). These are internal to the domain — no wire-contract risk.
- Assert on `result.IsSuccess` / `result.IsError` and then extract the value.
- Test every state-machine transition: which transitions succeed, which fail, and which error variant is returned.

## Concurrency tests

Test Marten's optimistic concurrency (`FetchForWriting`) by opening two sessions against the same stream and verifying the second writer gets a `ConcurrencyException` or `EventStreamUnexpectedMaxEventIdException`.

Also verify that separate streams (e.g., comment stream vs. job offer stream) don't conflict — a comment append must not block a concurrent status change on the same job offer.

These tests inject `TestWebApplicationFactory` for the `IDocumentStore` but don't use `HttpClient` — they talk directly to Marten.

## Test helpers

| Helper | Purpose |
|--------|---------|
| `TestWebApplicationFactory` | Shared test host with Testcontainers PostgreSQL and faked externals |
| `JwtTestHelper.GenerateToken(userId, email, isAdmin)` | Mints a valid JWT for any test identity |
| `FakeJwksHandler` | Returns test JWKS so the auth pipeline validates test tokens |
| `InMemoryStorageService` | In-memory file storage (upload/download) |
| `AlwaysPassTurnstileValidator` | Skips Turnstile captcha validation |
| `FakeSupabaseAdminService` | Controllable Supabase admin stub (`NextChangePasswordError`, `LastChangePasswordCall`) |
| `NoOpUserInfoService` | Skips user info lookups |

## Adding a new test

1. **API test for a new endpoint**: add to the existing feature test class (e.g., `JobOfferApiTests`) or create a new `{Feature}ApiTests` class with the same `IClassFixture<TestWebApplicationFactory>` pattern. Use anonymous objects for requests, `JsonElement` for responses.
2. **Domain test for a new aggregate rule**: add to `{Aggregate}AggregateTests`. Use domain types directly. Test both success and every error variant.
3. **New test helper/fake**: add to `Helpers/` and register in `TestWebApplicationFactory.ConfigureServices`.
