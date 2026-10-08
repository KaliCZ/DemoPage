import { test, expect, type APIRequestContext } from "@playwright/test";

/**
 * E2E test for the forgot-password flow against local Supabase:
 *   1. Request a reset link from the sign-in dialog
 *   2. Open the emailed link, choose a new password, and land back on the page the reset started from
 *   3. Only the new password signs in afterwards
 *   4. A link that was already used is rejected, with a way to request a fresh one
 *
 * Requires local Supabase (including its Mailpit mail catcher) and the frontend at :4321.
 */

const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";
const MAILPIT_URL = process.env.MAILPIT_URL || "http://127.0.0.1:54324";

const resetUser = {
  email: `e2e-reset-${Date.now()}@test.local`,
  password: "old-password-123",
  newPassword: "new-password-456",
};

const usedLinkUser = {
  email: `e2e-reset-used-link-${Date.now()}@test.local`,
  password: "old-password-123",
};

/** Polls the mail catcher for the password-reset email to `to` and returns the verification link in it. */
async function waitForResetLink(request: APIRequestContext, to: string): Promise<string> {
  let link = "";
  await expect
    .poll(
      async () => {
        const search = await request.get(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`);
        if (!search.ok()) return "";
        const [message] = (await search.json()).messages ?? [];
        if (!message) return "";
        const detail = await (await request.get(`${MAILPIT_URL}/api/v1/message/${message.ID}`)).json();
        link = detail.Text.match(/https?:\/\/\S+\/auth\/v1\/verify\?\S+/)?.[0] ?? "";
        return link;
      },
      { timeout: 30000, message: `password reset email to ${to}` },
    )
    .not.toBe("");
  return link;
}

test.describe("Password Reset Flow", () => {
  test.beforeAll(async ({ request }) => {
    for (const user of [resetUser, usedLinkUser]) {
      const response = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
        headers: {
          Authorization: `Bearer ${SUPABASE_SERVICE_ROLE_KEY}`,
          apikey: SUPABASE_SERVICE_ROLE_KEY,
          "Content-Type": "application/json",
        },
        data: { email: user.email, password: user.password, email_confirm: true },
      });
      expect(response.ok(), `Failed to create test user: ${await response.text()}`).toBeTruthy();
    }
  });

  test("forgot password → emailed link → new password → back where the reset started", async ({ page, request }) => {
    await page.goto("/about");
    await page.locator("#auth-sign-in-desktop").click();
    const dialog = page.locator("#auth-dialog");
    await expect(dialog).toBeVisible();

    await dialog.getByRole("button", { name: "Forgot password?" }).click();
    await expect(dialog.locator("#auth-dialog-title")).toHaveText("Reset Password");
    await expect(dialog.locator("#auth-password")).toBeHidden();

    await dialog.locator("#auth-email").fill(resetUser.email);
    await dialog.getByRole("button", { name: "Send Reset Link" }).click();
    // Submitting waits on a Turnstile challenge first, which the dialog allows up to 15s.
    await expect(dialog).toBeHidden({ timeout: 20000 });
    await expect(page.getByText("a password reset link is on its way")).toBeVisible();

    const link = await waitForResetLink(request, resetUser.email);
    await page.goto(link);
    await expect(page).toHaveURL(/\/reset-password\?next=%2Fabout$/);
    await expect(page.locator("#reset-form-section")).toBeVisible();
    await expect(page.locator("#reset-account-email")).toHaveValue(resetUser.email);

    await page.fill("#reset-new-password", resetUser.newPassword);
    await page.fill("#reset-confirm-password", "a-different-password");
    await page.click("#reset-submit");
    await expect(page.locator("#reset-error")).toHaveText("Passwords do not match.");

    await page.fill("#reset-confirm-password", resetUser.newPassword);
    await page.click("#reset-submit");
    await expect(page).toHaveURL(/\/about$/);
    await expect(page.getByText("Your password has been changed")).toBeVisible();

    const signInErrors = await page.evaluate(async ({ email, password, newPassword }) => {
      const supabase = await (window as any).__supabaseReady;
      await supabase.auth.signOut();
      const withOldPassword = await supabase.auth.signInWithPassword({ email, password });
      const withNewPassword = await supabase.auth.signInWithPassword({ email, password: newPassword });
      return { oldPassword: withOldPassword.error?.message ?? null, newPassword: withNewPassword.error?.message ?? null };
    }, resetUser);
    expect(signInErrors.oldPassword).not.toBeNull();
    expect(signInErrors.newPassword).toBeNull();
  });

  test("an already used link offers to send a new one", async ({ page, request, baseURL }) => {
    const redirectTo = `${baseURL}/auth/callback?next=${encodeURIComponent("/reset-password")}`;
    const recover = await request.post(`${SUPABASE_URL}/auth/v1/recover?redirect_to=${encodeURIComponent(redirectTo)}`, {
      headers: { apikey: SUPABASE_SERVICE_ROLE_KEY, "Content-Type": "application/json" },
      data: { email: usedLinkUser.email },
    });
    expect(recover.ok(), `Failed to request a reset link: ${await recover.text()}`).toBeTruthy();

    const link = await waitForResetLink(request, usedLinkUser.email);
    // Spend the one-time token, as if the link had already been opened once.
    await request.get(link, { maxRedirects: 0 });

    await page.goto(link);
    await expect(page).toHaveURL(/\/reset-password$/);
    await expect(page.locator("#reset-invalid")).toBeVisible();
    await expect(page.locator("#reset-form-section")).toBeHidden();

    await page.getByRole("button", { name: "Send a New Link" }).click();
    const dialog = page.locator("#auth-dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.locator("#auth-dialog-title")).toHaveText("Reset Password");
  });
});
