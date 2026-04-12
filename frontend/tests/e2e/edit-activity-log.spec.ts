import { test, expect } from '@playwright/test';

/**
 * E2E test for the edit activity log:
 *   1. Create a test user and submit a job offer
 *   2. Edit the offer (change at least 2 fields)
 *   3. Verify the activity log shows structured field changes
 *      with old and new values
 *
 * Requires:
 *   - Local Supabase running (npm run dev:supabase)
 *   - Backend API at http://localhost:5000
 *   - Frontend at http://localhost:4321
 */

const SUPABASE_URL = process.env.SUPABASE_URL || 'http://localhost:54321';
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU';

const testUser = {
  email: `e2e-edit-${Date.now()}@test.local`,
  password: 'test-password-123',
  fullName: 'E2E Edit User',
};

test.describe('Edit Activity Log', () => {
  test.beforeAll(async ({ request }) => {
    const response = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
      headers: {
        'Authorization': `Bearer ${SUPABASE_SERVICE_ROLE_KEY}`,
        'apikey': SUPABASE_SERVICE_ROLE_KEY,
        'Content-Type': 'application/json',
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

  test('submit → edit 2 fields → activity log shows old and new values', async ({ page }) => {
    // --- Step 1: Submit a job offer via the hire-me form ---
    await page.goto('/hire-me');
    await page.evaluate(async ({ email, password }) => {
      const supabase = (window as any).__supabase;
      if (!supabase) throw new Error('Supabase client not available');
      const { error } = await supabase.auth.signInWithPassword({ email, password });
      if (error) throw new Error(`Sign-in failed: ${error.message}`);
    }, { email: testUser.email, password: testUser.password });

    await expect(page.locator('#job-offer-form-section')).toBeVisible();

    await page.fill('#companyName', 'Original Corp');
    await page.fill('#contactName', 'Alice');
    await page.fill('#contactEmail', 'alice@original.com');
    await page.fill('#jobTitle', 'Developer');
    await page.fill('#description', 'A developer role at Original Corp.');

    // Inject Turnstile token
    await page.evaluate(() => {
      let input = document.querySelector<HTMLInputElement>('[name="cf-turnstile-response"]');
      if (!input) {
        input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'cf-turnstile-response';
        document.getElementById('job-offer-form')?.appendChild(input);
      }
      input.value = 'test-token';
    });

    await page.click('#submit-btn');
    await expect(page).toHaveURL(/\/job-offers/, { timeout: 15000 });

    // --- Step 2: Verify the detail view is open ---
    await expect(page.locator('#offer-detail-section')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('#offer-detail')).toContainText('Original Corp');

    // --- Step 3: Click Edit and change 2 fields ---
    await expect(page.locator('#edit-btn')).toBeVisible();
    await page.click('#edit-btn');

    await expect(page.locator('#edit-form-section')).toBeVisible();

    // Change company name, job title, and description
    await page.fill('#edit-companyName', 'Updated Corp');
    await page.fill('#edit-jobTitle', 'Senior Developer');
    await page.fill('#edit-description', 'An updated description for the role.');

    // Submit the edit
    await page.locator('#edit-form button[type="submit"]').click();

    // Wait for the detail view to reload with updated values
    await expect(page.locator('#offer-detail')).toContainText('Updated Corp', { timeout: 10000 });
    await expect(page.locator('#offer-detail')).toContainText('Senior Developer');

    // --- Step 4: Verify the activity log shows structured field changes ---
    const historySection = page.locator('#history-section');
    await expect(historySection).toBeVisible();

    const historyList = page.locator('#history-list');

    // Verify old and new values appear for short fields
    await expect(historyList).toContainText('Original Corp');
    await expect(historyList).toContainText('Updated Corp');
    await expect(historyList).toContainText('Developer');
    await expect(historyList).toContainText('Senior Developer');
    await expect(historyList).toContainText('→');

    // Verify the localized field labels are shown
    await expect(historyList).toContainText('Company');
    await expect(historyList).toContainText('Job Title');

    // --- Step 5: Verify expandable description diff ---
    // Description is a long-text field: shows "Description changed" as a toggle
    const descToggle = historyList.locator('.diff-toggle', { hasText: 'Description' });
    await expect(descToggle).toBeVisible();
    await expect(descToggle).toContainText('changed');

    // The diff panel should be hidden by default
    const diffPanel = historyList.locator('.diff-toggle + div', { hasText: 'Before' }).first();
    await expect(diffPanel).toBeHidden();

    // Click to expand
    await descToggle.click();
    await expect(diffPanel).toBeVisible();

    // Verify the old and new description text are shown
    await expect(diffPanel).toContainText('A developer role at Original Corp.');
    await expect(diffPanel).toContainText('An updated description for the role.');

    // Click again to collapse
    await descToggle.click();
    await expect(diffPanel).toBeHidden();
  });
});
