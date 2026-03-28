import { test, expect } from '@playwright/test';

test.describe('Page rendering', () => {
  test('home page loads in English', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/kalandra\.tech/);
    await expect(page.locator('nav')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Hire Me' })).toBeVisible();
  });

  test('home page loads in Czech', async ({ page }) => {
    await page.goto('/cs/');
    await expect(page).toHaveTitle(/kalandra\.tech/);
    await expect(page.getByRole('link', { name: 'Najměte mě' })).toBeVisible();
  });

  test('hire-me page shows login prompt when not authenticated', async ({ page }) => {
    await page.goto('/hire-me');
    await expect(page.locator('#login-prompt')).toBeVisible();
    await expect(page.locator('#job-offer-form-section')).toBeHidden();
  });

  test('job-offers page shows login prompt when not authenticated', async ({ page }) => {
    await page.goto('/job-offers');
    await expect(page.locator('#login-prompt')).toBeVisible();
    await expect(page.locator('#offers-list-section')).toBeHidden();
  });

  test('about page loads', async ({ page }) => {
    await page.goto('/about');
    await expect(page).toHaveTitle(/About/);
  });

  test('project page loads', async ({ page }) => {
    await page.goto('/project');
    await expect(page).toHaveTitle(/Project/);
  });
});

test.describe('Navigation', () => {
  test('can navigate between pages via nav links', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'About Me' }).first().click();
    await expect(page).toHaveURL('/about');

    await page.getByRole('link', { name: 'Hire Me' }).first().click();
    await expect(page).toHaveURL('/hire-me');
  });

  test('language switcher changes language', async ({ page }) => {
    await page.goto('/about');
    // Hover over language picker to open dropdown
    const langPicker = page.locator('.group').filter({ has: page.locator('[aria-label="Change language"]') }).first();
    await langPicker.hover();
    await page.getByRole('menuitem', { name: 'Čeština' }).first().click();
    await expect(page).toHaveURL('/cs/about');
  });
});

test.describe('Dark mode', () => {
  test('toggle switches to dark mode', async ({ page }) => {
    await page.goto('/');
    const html = page.locator('html');
    await expect(html).not.toHaveClass(/dark/);

    await page.locator('#theme-toggle').click();
    await expect(html).toHaveClass(/dark/);

    await page.locator('#theme-toggle').click();
    await expect(html).not.toHaveClass(/dark/);
  });
});
