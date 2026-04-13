# Backend Testing Guide

xUnit v3 with Microsoft.Testing.Platform.

## Table of contents

- [Test project naming](#test-project-naming)
- [Test infrastructure](#test-infrastructure)
- [The contract-detection rule](#the-contract-detection-rule)
- [Writing tests](#writing-tests)

## Test project naming

- **`{Project}.Tests`** — unit tests for that project. No external dependencies (no Testcontainers, no `WebApplicationFactory`). Example: `Kalandra.JobOffers.Tests` for aggregate decide/apply tests.
- **`{Project}.IntegrationTests`** — integration tests that need real infrastructure. Example: `Kalandra.Api.IntegrationTests` uses Testcontainers PostgreSQL + `TestWebApplicationFactory` for full HTTP round-trip tests.

Each test project lives in `backend/tests/` and references only the project it's testing (plus `Kalandra.Infrastructure` if needed for shared types like `CurrentUser`).

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

## Writing tests

**API integration tests** — one test class per feature area, injecting `TestWebApplicationFactory` as `IClassFixture`. Each test should authenticate with a unique email to avoid cross-test interference (the factory shares a single database). Use helper methods to generate test data and to make common assertions reusable. What to cover:
- Auth gates (401, 403).
- Happy paths with full response shape assertion.
- Validation failures (400 with expected error codes).
- Authorization boundaries (owner vs. other user vs. admin).
- Side effects (history entries, comments visible to both parties).

**Domain aggregate tests** — pure unit tests that exercise decide/apply directly. No HTTP, no database. Use domain types directly (no wire-contract risk). Test every state-machine transition: which succeed, which fail, and which error variant is returned.

**Concurrency tests** — verify Marten's optimistic concurrency by opening two sessions against the same stream and confirming the second writer gets a concurrency exception. Also verify that separate streams (e.g. comments vs. job offer) don't conflict.

## Running tests locally

Use `npm test` from the repo root — it runs backend, frontend, and E2E tests. This is what CI runs.
