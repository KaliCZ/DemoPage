import { test, expect } from '@playwright/test';

/**
 * E2E test for the full hire-me flow:
 *   1. See login prompt on hire-me page
 *   2. Sign in (via local Supabase email/password)
 *   3. Fill and submit the job offer form
 *   4. See success confirmation
 *   5. Navigate to job-offers and verify the submission appears
 *
 * Requires:
 *   - Local Supabase running (npx supabase start)
 *   - Backend API at http://localhost:5000
 *   - Frontend at http://localhost:4321
 */

const SUPABASE_URL = process.env.SUPABASE_URL || 'http://localhost:54321';
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU';

const testUser = {
  email: `e2e-${Date.now()}@test.local`,
  password: 'test-password-123',
  fullName: 'E2E Test User',
};

test.describe('Hire Me Flow', () => {
  test.beforeAll(async ({ request }) => {
    // Create a test user via Supabase admin API
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

  test('login prompt → sign in → submit form → success → verify in job-offers', async ({ page }) => {
    // 1. Navigate to hire-me and verify login prompt is shown
    await page.goto('/hire-me');
    await expect(page.locator('#login-prompt')).toBeVisible();
    await expect(page.locator('#login-prompt')).toContainText('Authentication Required');
    await expect(page.locator('#job-offer-form-section')).toBeHidden();

    // 2. Sign in programmatically via the page's Supabase client
    await page.evaluate(async ({ email, password }) => {
      const supabase = (window as any).__supabase;
      if (!supabase) throw new Error('Supabase client not available on window.__supabase');
      const { error } = await supabase.auth.signInWithPassword({ email, password });
      if (error) throw new Error(`Sign-in failed: ${error.message}`);
    }, { email: testUser.email, password: testUser.password });

    // 3. Verify form appears and login prompt is hidden
    await expect(page.locator('#job-offer-form-section')).toBeVisible();
    await expect(page.locator('#login-prompt')).toBeHidden();

    // Verify the nav shows the user profile (avatar/name), not the sign-in button
    await expect(page.locator('#auth-profile-desktop')).toBeVisible();
    await expect(page.locator('#auth-sign-in-desktop')).toBeHidden();

    // 4. Fill out the job offer form
    await page.fill('#companyName', 'E2E Test Corp');
    await page.fill('#contactName', 'E2E Tester');
    await page.fill('#contactEmail', 'tester@e2etest.com');
    await page.fill('#jobTitle', 'Senior Engineer');
    await page.fill('#description', 'This is an automated E2E test submission to verify the full hire-me flow.');
    await page.fill('#salaryRange', '$100k - $150k');
    await page.fill('#location', 'Remote');
    await page.check('#isRemote');

    // 5. Submit the form
    await page.click('#submit-btn');

    // 6. Verify success state (allow extra time for API call)
    await expect(page.locator('#form-success')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('#form-success')).toContainText('Offer Submitted');
    await expect(page.locator('#job-offer-form-section')).toBeHidden();

    // 7. Navigate to job-offers via the success page link
    await page.click('#form-success a');
    await expect(page).toHaveURL('/job-offers');

    // 8. Verify the submission appears in the offers list
    await expect(page.locator('#offers-list-section')).toBeVisible();
    await expect(page.locator('#login-prompt')).toBeHidden();
    await expect(page.locator('#offers-grid')).toBeVisible();
    await expect(page.locator('#offers-grid')).toContainText('Senior Engineer');
    await expect(page.locator('#offers-grid')).toContainText('E2E Test Corp');

    // 9. Click the offer to see the detail view
    await page.locator('#offers-grid > div').first().click();
    await expect(page.locator('#offer-detail-section')).toBeVisible();
    await expect(page.locator('#offer-detail')).toContainText('Senior Engineer');
    await expect(page.locator('#offer-detail')).toContainText('E2E Test Corp');
    await expect(page.locator('#offer-detail')).toContainText('tester@e2etest.com');
    // Verify the offer is in "Submitted" status
    await expect(page.locator('#offer-detail')).toContainText('Submitted');
  });
});
