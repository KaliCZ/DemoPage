import { test, expect } from "@playwright/test";

/**
 * E2E test for the blog reactions + comments flow against the real backend:
 *   1. Anonymous visitor sees the post with live reaction counts and comments
 *   2. Interacting while signed out opens the auth dialog; the API returns 401
 *   3. After sign-in: toggle a reaction (persists across reload), post a
 *      comment, reply to it, and delete the reply (tombstone)
 *
 * Requires local Supabase, backend API at :5000, and frontend at :4321.
 */

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const SLUG = "zero-code-validations-in-your-dotnet-api";
const POST_PATH = `/blog/${SLUG}`;

const testUser = {
  email: `e2e-blog-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Blog User",
};

test.describe("Blog Flow", () => {
  test.beforeAll(async ({ request }) => {
    const response = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
      headers: {
        Authorization: `Bearer ${SUPABASE_SERVICE_ROLE_KEY}`,
        apikey: SUPABASE_SERVICE_ROLE_KEY,
        "Content-Type": "application/json",
      },
      data: {
        email: testUser.email,
        password: testUser.password,
        email_confirm: true,
        user_metadata: { full_name: testUser.fullName },
      },
    });
    expect(response.ok(), `Failed to create test user: ${await response.text()}`).toBeTruthy();
  });

  test("anonymous visitor sees reactions and comments but must sign in to interact", async ({ page, request }) => {
    await page.goto(POST_PATH);

    const reactions = page.locator('section[aria-label="Was this useful?"]');
    await expect(reactions.getByRole("button", { name: "Thumbs up" })).toBeVisible();
    await expect(reactions.getByRole("button", { name: "Thumbs down" })).toBeVisible();

    const comments = page.locator('section[aria-label="Comments"]');
    await expect(comments.getByRole("button", { name: "Sign In" })).toBeVisible();

    // A signed-out reaction click opens the auth dialog instead of calling the API.
    await reactions.getByRole("button", { name: "Thumbs up" }).click();
    await expect(page.locator("#auth-dialog")).toBeVisible();

    // The API itself refuses anonymous writes.
    const toggleResponse = await request.post(`${API_URL}/api/blog/${SLUG}/reactions/toggle`, {
      data: { kind: "ThumbsUp" },
    });
    expect(toggleResponse.status()).toBe(401);

    const commentResponse = await request.post(`${API_URL}/api/blog/${SLUG}/comments`, {
      data: { content: "anonymous comment" },
    });
    expect(commentResponse.status()).toBe(401);
  });

  test("signed-in user reacts, comments, replies, and deletes", async ({ page }) => {
    await page.goto(POST_PATH);

    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: testUser.email, password: testUser.password },
    );

    // Reaction toggles on and survives a reload (state comes from the backend).
    const reactions = page.locator('section[aria-label="Was this useful?"]');
    const thumbsUp = reactions.getByRole("button", { name: "Thumbs up" });
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "false");

    // The UI flips optimistically, so wait for the server write to land before
    // reloading — a reload mid-flight would abort the request and lose the toggle.
    const toggleSettled = page.waitForResponse((response) => response.url().includes("/reactions/toggle"));
    await thumbsUp.click();
    expect((await toggleSettled).ok()).toBeTruthy();
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "true");

    await page.reload();
    await expect(page.locator('section[aria-label="Was this useful?"]').getByRole("button", { name: "Thumbs up" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    // Post a top-level comment.
    const comments = page.locator('section[aria-label="Comments"]');
    const commentText = `E2E comment ${Date.now()}`;
    await comments.getByRole("textbox").fill(commentText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    const commentItem = comments.locator("li").filter({ hasText: commentText }).first();
    await expect(commentItem).toBeVisible();
    await expect(commentItem).toContainText(testUser.fullName);

    // Reply to it.
    await commentItem.getByRole("button", { name: "Reply" }).click();
    const replyText = `E2E reply ${Date.now()}`;
    await comments.getByRole("textbox").fill(replyText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    const replyItem = comments.locator("li").filter({ hasText: replyText }).first();
    await expect(replyItem).toBeVisible();

    // Delete the reply — inline confirm, then the tombstone placeholder takes its place.
    await replyItem.getByRole("button", { name: "Delete" }).click();
    await replyItem.getByRole("button", { name: "Delete" }).click();
    await expect(comments.getByText(replyText)).toHaveCount(0);
    await expect(comments.getByText("[deleted]").first()).toBeVisible();
  });
});
