import { test, expect } from '@playwright/test';

/**
 * E2E tests for job-offers pagination.
 *
 * Requires:
 *   - Local Supabase running (npm run dev:supabase)
 *   - Backend API at http://localhost:5000
 *   - Frontend at http://localhost:4321
 */

const API_URL = process.env.PUBLIC_API_URL || 'http://localhost:5000';
const SUPABASE_URL = process.env.SUPABASE_URL || 'http://localhost:54321';
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU';

const testUser = {
  email: `e2e-pagination-${Date.now()}@test.local`,
  password: 'test-password-123',
  fullName: 'Pagination Test User',
};

async function signIn(page: any) {
  await page.evaluate(async ({ email, password }: { email: string; password: string }) => {
    const supabase = (window as any).__supabase;
    if (!supabase) throw new Error('Supabase client not available on window.__supabase');
    const { error } = await supabase.auth.signInWithPassword({ email, password });
    if (error) throw new Error(`Sign-in failed: ${error.message}`);
  }, { email: testUser.email, password: testUser.password });
}

async function createOfferViaApi(token: string, suffix: string) {
  const form = new FormData();
  form.append('cf-turnstile-response', 'test-token');
  form.append('CompanyName', `Pagination Corp ${suffix}`);
  form.append('ContactName', 'Test User');
  form.append('ContactEmail', 'test@pagination.com');
  form.append('JobTitle', `Engineer ${suffix}`);
  form.append('Description', 'Created by pagination E2E test to verify pagination controls work.');
  form.append('IsRemote', 'false');

  const res = await fetch(`${API_URL}/api/job-offers`, {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${token}` },
    body: form,
  });
  if (!res.ok) throw new Error(`Failed to create offer: ${res.status}`);
}

test.describe('Job Offers Pagination', () => {
  let accessToken: string;

  test.beforeAll(async ({ request }) => {
    // Create test user via Supabase admin API
    const createRes = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
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
    expect(createRes.ok(), `Failed to create test user: ${await createRes.text()}`).toBeTruthy();

    // Sign in to get access token for seeding offers
    const signInRes = await request.post(`${SUPABASE_URL}/auth/v1/token?grant_type=password`, {
      headers: {
        'apikey': SUPABASE_SERVICE_ROLE_KEY,
        'Content-Type': 'application/json',
      },
      data: { email: testUser.email, password: testUser.password },
    });
    expect(signInRes.ok()).toBeTruthy();
    const signInData = await signInRes.json();
    accessToken = signInData.access_token;

    // Create enough offers to trigger pagination (pageSize=10, create 11)
    const promises = [];
    for (let i = 1; i <= 11; i++) {
      promises.push(createOfferViaApi(accessToken, String(i)));
    }
    await Promise.all(promises);
  });

  test('pagination controls appear when results exceed page size', async ({ page }) => {
    await page.goto('/job-offers');
    await signIn(page);

    // Wait for offers to load
    await expect(page.locator('#offers-grid')).toBeVisible({ timeout: 15000 });

    // Pagination controls should be visible (11 offers > default pageSize=10)
    await expect(page.locator('#pagination-controls')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('#pagination-indicator')).toContainText('Page 1 of 2');

    // Previous button should be disabled on first page
    await expect(page.locator('#pagination-prev')).toBeDisabled();
    // Next button should be enabled
    await expect(page.locator('#pagination-next')).toBeEnabled();
  });

  test('clicking next navigates to page 2 and back', async ({ page }) => {
    await page.goto('/job-offers');
    await signIn(page);

    await expect(page.locator('#offers-grid')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('#pagination-controls')).toBeVisible({ timeout: 5000 });

    // Click next
    await page.click('#pagination-next');

    // Should now be on page 2
    await expect(page.locator('#pagination-indicator')).toContainText('Page 2 of 2');
    await expect(page.locator('#pagination-prev')).toBeEnabled();
    await expect(page.locator('#pagination-next')).toBeDisabled();

    // Page 2 should have 1 offer (21 total, 20 per page)
    await expect(page.locator('#offers-grid > div')).toHaveCount(1);

    // Click previous
    await page.click('#pagination-prev');

    // Should be back on page 1
    await expect(page.locator('#pagination-indicator')).toContainText('Page 1 of 2');
    await expect(page.locator('#pagination-prev')).toBeDisabled();
    await expect(page.locator('#pagination-next')).toBeEnabled();
  });

  test('filter change resets to page 1', async ({ page }) => {
    await page.goto('/job-offers');
    await signIn(page);

    await expect(page.locator('#offers-grid')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('#pagination-controls')).toBeVisible({ timeout: 5000 });

    // Go to page 2
    await page.click('#pagination-next');
    await expect(page.locator('#pagination-indicator')).toContainText('Page 2 of 2');

    // Apply a status filter — should reset to page 1
    await page.click('#status-filter-toggle');
    await page.locator('.status-checkbox[value="Submitted"]').check();

    // After filter change, if there are still multiple pages, we should be on page 1
    // If filter narrows results to single page, pagination hides — both are correct
    const controls = page.locator('#pagination-controls');
    const isVisible = await controls.isVisible();
    if (isVisible) {
      await expect(page.locator('#pagination-indicator')).toContainText(/Page 1 of/);
    }
  });
});
