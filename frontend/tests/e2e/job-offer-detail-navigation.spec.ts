import { test, expect } from "@playwright/test";

/**
 * E2E test for the split list/detail pages:
 *   1. The list links each offer card to /job-offers/detail?id=...
 *   2. The detail page renders the offer for a direct (shareable) URL
 *   3. The back link returns to the list
 *
 * Requires:
 *   - Local Supabase running (npm run dev:supabase)
 *   - Backend API at http://localhost:5000
 *   - Frontend at http://localhost:4321
 */

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const testUser = {
  email: `e2e-detail-nav-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "Detail Nav Test User",
};

async function signIn(page: any) {
  await page.evaluate(
    async ({ email, password }: { email: string; password: string }) => {
      const supabase = await (window as any).__supabaseReady;
      if (!supabase) throw new Error("Supabase client not available via window.__supabaseReady");
      const { error } = await supabase.auth.signInWithPassword({
        email,
        password,
      });
      if (error) throw new Error(`Sign-in failed: ${error.message}`);
    },
    { email: testUser.email, password: testUser.password },
  );
}

test.describe("Job Offer Detail Navigation", () => {
  let offerId: string;

  test.beforeAll(async ({ request }) => {
    const createRes = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
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
    expect(createRes.ok(), `Failed to create test user: ${await createRes.text()}`).toBeTruthy();

    const signInRes = await request.post(`${SUPABASE_URL}/auth/v1/token?grant_type=password`, {
      headers: {
        apikey: SUPABASE_SERVICE_ROLE_KEY,
        "Content-Type": "application/json",
      },
      data: { email: testUser.email, password: testUser.password },
    });
    expect(signInRes.ok()).toBeTruthy();
    const { access_token } = await signInRes.json();

    const form = new FormData();
    form.append("cf-turnstile-response", "test-token");
    form.append("CompanyName", "Detail Nav Corp");
    form.append("ContactName", "Nav Tester");
    form.append("ContactEmail", "nav@detailnav.com");
    form.append("JobTitle", "Navigation Engineer");
    form.append("Description", "Created by the detail-navigation E2E test to verify list/detail routing.");
    form.append("IsRemote", "false");

    const res = await fetch(`${API_URL}/api/job-offers`, {
      method: "POST",
      headers: { Authorization: `Bearer ${access_token}` },
      body: form,
    });
    expect(res.ok, `Failed to create offer: ${res.status}`).toBeTruthy();
    ({ id: offerId } = await res.json());
  });

  test("list card links to the detail page, back link returns to the list", async ({ page }) => {
    await page.goto("/job-offers");
    await signIn(page);

    // The list card is a real link to the detail page
    const card = page.locator("#offers-grid > a", { hasText: "Detail Nav Corp" }).first();
    await expect(card).toBeVisible({ timeout: 15000 });
    await expect(card).toHaveAttribute("href", `/job-offers/detail?id=${offerId}`);

    await card.click();
    await expect(page).toHaveURL(`/job-offers/detail?id=${offerId}`);
    await expect(page.locator("#offer-detail")).toContainText("Navigation Engineer", { timeout: 10000 });
    await expect(page.locator("#offer-detail")).toContainText("Detail Nav Corp");

    // Back link returns to the list
    await page.click("#back-to-list");
    await expect(page).toHaveURL(/\/job-offers$/);
    await expect(page.locator("#offers-grid")).toBeVisible({ timeout: 15000 });
  });

  test("direct detail URL renders the offer after sign-in", async ({ page }) => {
    await page.goto(`/job-offers/detail?id=${offerId}`);
    await expect(page.locator("#login-prompt")).toBeVisible();

    await signIn(page);
    await expect(page.locator("#offer-detail-section")).toBeVisible();
    await expect(page.locator("#offer-detail")).toContainText("Navigation Engineer", { timeout: 10000 });
  });
});
