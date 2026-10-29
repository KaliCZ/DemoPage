import { test, expect } from "@playwright/test";
import * as path from "path";
import * as fs from "fs";
import * as os from "os";

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

const testUser = {
  email: `e2e-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Test User",
};

test.describe("Hire Me Flow", () => {
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

  test("login prompt → sign in → submit form → success → verify in job-offers", async ({ page }) => {
    await page.goto("/hire-me");
    await expect(page.locator("#login-prompt")).toBeVisible();
    await expect(page.locator("#login-prompt")).toContainText("Authentication Required");
    await expect(page.locator("#job-offer-form-section")).toBeHidden();

    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available on window.__supabase");
        const { error } = await supabase.auth.signInWithPassword({
          email,
          password,
        });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: testUser.email, password: testUser.password },
    );

    await expect(page.locator("#job-offer-form-section")).toBeVisible();
    await expect(page.locator("#login-prompt")).toBeHidden();

    await expect(page.locator("#auth-profile-desktop")).toBeVisible();
    await expect(page.locator("#auth-sign-in-desktop")).toBeHidden();

    await expect(page.locator("#contactName")).toHaveValue(testUser.fullName);
    await expect(page.locator("#contactEmail")).toHaveValue(testUser.email);

    await page.fill("#companyName", "E2E Test Corp");
    await page.fill("#contactName", "E2E Tester");
    await page.fill("#contactEmail", "tester@e2etest.com");
    await page.fill("#jobTitle", "Senior Engineer");
    await page.fill("#description", "This is an automated E2E test submission to verify the full hire-me flow.");
    await page.fill("#salaryRange", "$100k - $150k");
    await page.fill("#location", "Remote");
    await page.check("#isRemote");

    // The Turnstile widget doesn't auto-complete in headless CI.
    await page.evaluate(() => {
      let input = document.querySelector<HTMLInputElement>('[name="cf-turnstile-response"]');
      if (!input) {
        input = document.createElement("input");
        input.type = "hidden";
        input.name = "cf-turnstile-response";
        document.getElementById("job-offer-form")?.appendChild(input);
      }
      input.value = "test-token";
    });

    await page.click("#submit-btn");

    await expect(page.locator("#confirm-incomplete-dialog")).toBeVisible();
    await page.click("#confirm-incomplete-confirm");

    await expect(page).toHaveURL(/\/job-offers/, { timeout: 15000 });

    await expect(page.locator("#offer-detail-section")).toBeVisible({
      timeout: 10000,
    });
    await expect(page.locator("#offer-detail")).toContainText("Senior Engineer");
    await expect(page.locator("#offer-detail")).toContainText("E2E Test Corp");
    await expect(page.locator("#offer-detail")).toContainText("tester@e2etest.com");
    await expect(page.locator("#offer-detail")).toContainText("Submitted");
  });

  test("submit with attachment → verify file can be downloaded with matching content", async ({ page, request }) => {
    const fileContent = `E2E attachment test — ${Date.now()}`;
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "e2e-"));
    const filePath = path.join(tmpDir, "test-attachment.txt");
    fs.writeFileSync(filePath, fileContent, "utf-8");

    try {
      await page.goto("/hire-me");
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

      await expect(page.locator("#job-offer-form-section")).toBeVisible();

      await page.fill("#companyName", "Attachment Test Corp");
      await page.fill("#contactName", "File Tester");
      await page.fill("#contactEmail", "file@e2etest.com");
      await page.fill("#jobTitle", "Attachment Engineer");
      await page.fill("#description", "Testing that file uploads round-trip correctly.");

      const fileInput = page.locator("#attachments");
      await fileInput.setInputFiles(filePath);
      await expect(page.locator("#file-list")).toContainText("test-attachment.txt");

      // The Turnstile widget doesn't auto-complete in headless CI.
      await page.evaluate(() => {
        let input = document.querySelector<HTMLInputElement>('[name="cf-turnstile-response"]');
        if (!input) {
          input = document.createElement("input");
          input.type = "hidden";
          input.name = "cf-turnstile-response";
          document.getElementById("job-offer-form")?.appendChild(input);
        }
        input.value = "test-token";
      });

      await page.click("#submit-btn");
      await expect(page.locator("#confirm-incomplete-dialog")).toBeVisible();
      await page.click("#confirm-incomplete-confirm");
      await expect(page).toHaveURL(/\/job-offers/, { timeout: 15000 });

      const token = await page.evaluate(async () => {
        return await (window as any).__getAccessToken?.();
      });
      expect(token).toBeTruthy();

      const listResponse = await request.get(`${API_URL}/api/job-offers/mine`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(listResponse.ok()).toBeTruthy();
      const listData = await listResponse.json();
      const offer = listData.items.find((o: any) => o.companyName === "Attachment Test Corp");
      expect(offer).toBeTruthy();

      const detailResponse = await request.get(`${API_URL}/api/job-offers/${offer.id}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(detailResponse.ok()).toBeTruthy();
      const detail = await detailResponse.json();
      expect(detail.attachments).toHaveLength(1);
      expect(detail.attachments[0].fileName).toBe("test-attachment.txt");
      expect(detail.attachments[0].contentType).toBe("text/plain");

      const encodedName = encodeURIComponent("test-attachment.txt");
      const downloadResponse = await request.get(`${API_URL}/api/job-offers/${offer.id}/attachments/${encodedName}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(downloadResponse.ok(), `Download failed: ${downloadResponse.status()}`).toBeTruthy();

      const downloadedContent = await downloadResponse.text();
      expect(downloadedContent).toBe(fileContent);
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });
});
