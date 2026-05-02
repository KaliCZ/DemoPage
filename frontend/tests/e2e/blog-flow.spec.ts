import { test, expect, type Page } from "@playwright/test";

/**
 * E2E test for the signed-in blog interactions:
 *   1. Reaction toggle: click adds (aria-pressed=true, count +1); click again removes (back to start).
 *   2. Comment post: textarea → submit → comment appears in the list and persists across reload.
 *
 * Each run uses a fresh test user and posts a unique-tagged comment so concurrent
 * runs don't see each other's data. The reaction assertions are written
 * relative to the pre-test count rather than absolute, since the strongtypes-1-0
 * post is shared state on the live API.
 */

const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const POST_PATH = "/blog/strongtypes-1-0";

const testUser = {
  email: `e2e-blog-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Blog User",
};

async function signIn(page: Page) {
  await page.evaluate(
    async ({ email, password }) => {
      const supabase = await (window as { __supabaseReady?: Promise<unknown> }).__supabaseReady;
      if (!supabase) throw new Error("Supabase client not available on window.__supabaseReady");
      const { error } = await (
        supabase as {
          auth: { signInWithPassword: (c: { email: string; password: string }) => Promise<{ error: { message: string } | null }> };
        }
      ).auth.signInWithPassword({ email, password });
      if (error) throw new Error(`Sign-in failed: ${error.message}`);
    },
    { email: testUser.email, password: testUser.password },
  );
}

test.describe("Blog signed-in flow", () => {
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

  test("reaction toggle: add then remove returns to starting count", async ({ page }) => {
    await page.goto(POST_PATH);

    const button = page.locator('[data-reaction="ThumbsUp"]');
    const count = button.locator("[data-reaction-count]");
    await expect(button).toBeVisible();
    await signIn(page);

    // After auth-change the component refreshes state. A fresh test user has no
    // prior reaction on this post, so wait for that to settle before clicking.
    await expect(button).toHaveAttribute("aria-pressed", "false");
    const initialCount = parseInt(((await count.textContent()) ?? "0").trim(), 10);

    // 1. Add reaction
    await button.click();
    await expect(button).toHaveAttribute("aria-pressed", "true");
    await expect(count).toHaveText(new RegExp(`^\\s*${initialCount + 1}\\s*$`));

    // 2. Remove reaction (toggle off)
    await button.click();
    await expect(button).toHaveAttribute("aria-pressed", "false");
    await expect(count).toHaveText(new RegExp(`^\\s*${initialCount}\\s*$`));
  });

  test("comment post: appears in list and persists across reload", async ({ page }) => {
    const commentText = `E2E test comment ${Date.now()} ${Math.random().toString(36).slice(2, 8)}`;

    await page.goto(POST_PATH);
    await signIn(page);

    // Form is gated on auth — wait for it to become visible after sign-in.
    const form = page.locator("[data-comment-form]");
    await expect(form).toBeVisible();

    await page.locator("[data-comment-textarea]").fill(commentText);
    await page.locator("[data-comment-submit]").click();

    // The new comment renders in the list with the user's display name.
    const list = page.locator("[data-comments-list]");
    await expect(list.locator("li", { hasText: commentText })).toBeVisible();
    await expect(list.locator("li", { hasText: commentText })).toContainText(testUser.fullName);

    // Persists across reload (proves the backend round-tripped, not just a DOM insertion).
    await page.reload();
    await expect(page.locator("[data-comments-list] li", { hasText: commentText })).toBeVisible();
  });
});
