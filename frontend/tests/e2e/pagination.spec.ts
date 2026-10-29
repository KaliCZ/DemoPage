import { test, expect } from "@playwright/test";

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const testUser = {
  email: `e2e-pagination-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "Pagination Test User",
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

async function createOfferViaApi(token: string, suffix: string) {
  const form = new FormData();
  form.append("cf-turnstile-response", "test-token");
  form.append("CompanyName", `Pagination Corp ${suffix}`);
  form.append("ContactName", "Test User");
  form.append("ContactEmail", "test@pagination.com");
  form.append("JobTitle", `Engineer ${suffix}`);
  form.append("Description", "Created by pagination E2E test to verify pagination controls work.");
  form.append("IsRemote", "false");

  const res = await fetch(`${API_URL}/api/job-offers`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: form,
  });
  if (!res.ok) throw new Error(`Failed to create offer: ${res.status}`);
}

test.describe("Job Offers Pagination", () => {
  let accessToken: string;

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
    const signInData = await signInRes.json();
    accessToken = signInData.access_token;

    // One more than the list's PAGE_SIZE (JobOffersList.vue), so page 2 holds exactly one offer.
    const promises = [];
    for (let i = 1; i <= 11; i++) {
      promises.push(createOfferViaApi(accessToken, String(i)));
    }
    await Promise.all(promises);
  });

  test("pagination controls appear when results exceed page size", async ({ page }) => {
    await page.goto("/job-offers");
    await signIn(page);

    await expect(page.locator("#offers-grid")).toBeVisible({ timeout: 15000 });

    await expect(page.locator("#pagination-controls")).toBeVisible({
      timeout: 5000,
    });
    await expect(page.locator("#pagination-indicator")).toContainText("Page 1 of 2");

    await expect(page.locator("#pagination-prev")).toBeDisabled();
    await expect(page.locator("#pagination-next")).toBeEnabled();
  });

  test("clicking next navigates to page 2 and back", async ({ page }) => {
    await page.goto("/job-offers");
    await signIn(page);

    await expect(page.locator("#offers-grid")).toBeVisible({ timeout: 15000 });
    await expect(page.locator("#pagination-controls")).toBeVisible({
      timeout: 5000,
    });

    await page.click("#pagination-next");

    await expect(page.locator("#pagination-indicator")).toContainText("Page 2 of 2");
    await expect(page.locator("#pagination-prev")).toBeEnabled();
    await expect(page.locator("#pagination-next")).toBeDisabled();

    await expect(page.locator("#offers-grid > a")).toHaveCount(1);

    await page.click("#pagination-prev");

    await expect(page.locator("#pagination-indicator")).toContainText("Page 1 of 2");
    await expect(page.locator("#pagination-prev")).toBeDisabled();
    await expect(page.locator("#pagination-next")).toBeEnabled();
  });

  test("filter change resets to page 1", async ({ page }) => {
    await page.goto("/job-offers");
    await signIn(page);

    await expect(page.locator("#offers-grid")).toBeVisible({ timeout: 15000 });
    await expect(page.locator("#pagination-controls")).toBeVisible({
      timeout: 5000,
    });

    await page.click("#pagination-next");
    await expect(page.locator("#pagination-indicator")).toContainText("Page 2 of 2");

    await page.click("#status-filter-toggle");
    await page.locator('.status-checkbox[value="Submitted"]').check();

    // A filter that narrows results to one page hides the controls entirely.
    const controls = page.locator("#pagination-controls");
    const isVisible = await controls.isVisible();
    if (isVisible) {
      await expect(page.locator("#pagination-indicator")).toContainText(/Page 1 of/);
    }
  });
});
