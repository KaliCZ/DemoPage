import { test, expect } from '@playwright/test';

/**
 * E2E smoke tests that verify frontend + backend integration.
 * These require both services to be running:
 * - Frontend at http://localhost:4321
 * - Backend API at http://localhost:5000
 * - Local Supabase at http://localhost:54321
 */

test.describe('E2E Smoke Tests', () => {
  test('backend health endpoint is reachable and healthy', async ({ request }) => {
    const response = await request.get('http://localhost:5000/api/health');
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    expect(body.status).toBe('Healthy');
    expect(body).toHaveProperty('commitHash');
  });

  test('hire-me page shows login prompt with correct content', async ({ page }) => {
    await page.goto('/hire-me');
    await expect(page.locator('#login-prompt')).toBeVisible();
    await expect(page.locator('#login-prompt')).toContainText('Authentication Required');
    await expect(page.locator('#login-prompt button')).toContainText('Sign In');
    // Form should be hidden until authenticated
    await expect(page.locator('#job-offer-form-section')).toBeHidden();
  });

  test('job-offers page shows login prompt with correct content', async ({ page }) => {
    await page.goto('/job-offers');
    await expect(page.locator('#login-prompt')).toBeVisible();
    await expect(page.locator('#login-prompt')).toContainText('Sign In');
    await expect(page.locator('#offers-list-section')).toBeHidden();
  });

  test('unauthenticated API request returns 401', async ({ request }) => {
    const response = await request.post('http://localhost:5000/api/job-offers', {
      data: {
        companyName: 'Test',
        contactName: 'Test',
        contactEmail: 'test@test.com',
        jobTitle: 'Test',
        description: 'Test',
      },
    });
    expect(response.status()).toBe(401);
  });

  test('nav sign-in button is visible when not authenticated', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#auth-sign-in-desktop')).toBeVisible();
    await expect(page.locator('#auth-profile-desktop')).toBeHidden();
  });
});
