import { test, expect } from "@playwright/test";
import * as path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const testUser = {
  email: `e2e-avatar-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Avatar User",
};

test.describe("Avatar Flow", () => {
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

  test("upload avatar → verify in profile, nav, and comments", async ({ page }) => {
    await page.goto("/profile");
    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({
          email,
          password,
        });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: testUser.email, password: testUser.password },
    );

    await expect(page.locator("#profile-section")).toBeVisible();
    await expect(page.locator("#avatar-change-btn")).toBeVisible();

    await expect(page.locator("#avatar-preview-initial")).toBeVisible();
    await expect(page.locator("#avatar-preview-img")).toBeHidden();

    const avatarPath = path.join(__dirname, "..", "..", "public", "images", "pavel-portrait-200.webp");
    await page.locator("#avatar-file-input").setInputFiles(avatarPath);

    await expect(page.locator("#avatar-preview-img")).toBeVisible({
      timeout: 10000,
    });
    await expect(page.locator("#avatar-preview-initial")).toBeHidden();

    const previewSrc = await page.locator("#avatar-preview-img").getAttribute("src");
    expect(previewSrc).toContain("/storage/v1/object/public/avatars/");

    const token = await page.evaluate(async () => {
      return await (window as any).__getAccessToken?.();
    });
    expect(token).toBeTruthy();

    await page.goto("/hire-me");
    await page.fill("#companyName", "Avatar Test Corp");
    await page.fill("#contactName", "Avatar Tester");
    await page.fill("#contactEmail", "avatar@test.com");
    await page.fill("#jobTitle", "Avatar Engineer");
    await page.fill("#description", "Testing avatars in comments end-to-end.");
    // The real widget can render late and prepend an empty response input that the submit handler reads first, so stamp them all.
    const stampTurnstileToken = () =>
      page.evaluate(() => {
        const inputs = document.querySelectorAll<HTMLInputElement>('[name="cf-turnstile-response"]');
        if (inputs.length === 0) {
          const input = document.createElement("input");
          input.type = "hidden";
          input.name = "cf-turnstile-response";
          document.getElementById("job-offer-form")?.appendChild(input);
          input.value = "test-token";
          return;
        }
        inputs.forEach((input) => (input.value = "test-token"));
      });

    await stampTurnstileToken();
    await page.click("#submit-btn");
    await expect(page.locator("#confirm-incomplete-dialog")).toBeVisible();
    await stampTurnstileToken();
    await page.click("#confirm-incomplete-confirm");
    await expect(page).toHaveURL(/\/job-offers\/detail\?id=/, { timeout: 15000 });

    await expect(page.locator("#offer-detail-section")).toBeVisible({
      timeout: 10000,
    });

    await page.fill("#comment-input", "This comment should show my avatar");
    await page.click("#send-comment");

    const commentList = page.locator("#comments-list");
    await expect(commentList).toContainText("This comment should show my avatar", { timeout: 10000 });
    const commentAvatar = commentList.locator("img").first();
    await expect(commentAvatar).toBeVisible();
    const commentAvatarSrc = await commentAvatar.getAttribute("src");
    expect(commentAvatarSrc).toContain("/storage/v1/object/public/avatars/");
  });
});
