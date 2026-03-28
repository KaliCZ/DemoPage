import { test, expect } from '@playwright/test';

/**
 * E2E smoke tests that verify frontend + backend integration.
 * These require both services to be running:
 * - Frontend at http://localhost:4321
 * - Backend API at http://localhost:5000
 */

test.describe('E2E Smoke Tests', () => {
  test('backend health endpoint is reachable', async ({ request }) => {
    const response = await request.get('http://localhost:5000/api/health');
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    expect(body.status).toBe('healthy');
  });

  test('hire-me page loads and shows login prompt', async ({ page }) => {
    await page.goto('/hire-me');
    await expect(page.locator('#login-prompt')).toBeVisible();
    // Form should be hidden until authenticated
    await expect(page.locator('#job-offer-form-section')).toBeHidden();
  });

  test('job-offers page loads and shows login prompt', async ({ page }) => {
    await page.goto('/job-offers');
    await expect(page.locator('#login-prompt')).toBeVisible();
  });

  test('unauthenticated API request returns 401', async ({ request }) => {
    const response = await request.post('http://localhost:5000/api/job-offers', {
      data: { companyName: 'Test', contactName: 'Test', contactEmail: 'test@test.com', jobTitle: 'Test', description: 'Test' },
    });
    expect(response.status()).toBe(401);
  });
});
