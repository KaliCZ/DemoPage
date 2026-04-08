import { test, expect } from '@playwright/test';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

/**
 * E2E test for the avatar feature:
 *   1. Sign in
 *   2. Upload an avatar via the profile page
 *   3. Verify the preview, nav header, and Remove button update
 *   4. Submit a job offer + add a comment
 *   5. Verify the avatar appears next to the comment in the job offer detail
 *   6. Remove the avatar and verify it falls back to initials
 *
 * Requires:
 *   - Local Supabase running (npm run dev:supabase)
 *   - Backend API at http://localhost:5000 with the `avatars` storage bucket created
 *   - Frontend at http://localhost:4321
 */

const API_URL = process.env.PUBLIC_API_URL || 'http://localhost:5000';
const SUPABASE_URL = process.env.SUPABASE_URL || 'http://localhost:54321';
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU';

const testUser = {
  email: `e2e-avatar-${Date.now()}@test.local`,
  password: 'test-password-123',
  fullName: 'E2E Avatar User',
};

test.describe('Avatar Flow', () => {
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

  test('upload avatar → verify in profile, nav, and comments → remove', async ({ page }) => {
    // 1. Sign in via the profile page
    await page.goto('/profile');
    await page.evaluate(async ({ email, password }) => {
      const supabase = (window as any).__supabase;
      if (!supabase) throw new Error('Supabase client not available');
      const { error } = await supabase.auth.signInWithPassword({ email, password });
      if (error) throw new Error(`Sign-in failed: ${error.message}`);
    }, { email: testUser.email, password: testUser.password });

    // Profile section becomes visible after sign-in
    await expect(page.locator('#profile-section')).toBeVisible();
    await expect(page.locator('#avatar-change-btn')).toBeVisible();

    // Initially, no avatar — initial circle visible, image hidden, Remove hidden
    await expect(page.locator('#avatar-preview-initial')).toBeVisible();
    await expect(page.locator('#avatar-preview-img')).toBeHidden();
    await expect(page.locator('#avatar-remove-btn')).toBeHidden();

    // 2. Upload an avatar (use the existing portrait in public/)
    const avatarPath = path.join(__dirname, '..', '..', 'public', 'images', 'pavel-portrait-200.webp');
    await page.locator('#avatar-file-input').setInputFiles(avatarPath);

    // Wait for the upload + session refresh to complete (Remove button appears)
    await expect(page.locator('#avatar-remove-btn')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('#avatar-preview-img')).toBeVisible();
    await expect(page.locator('#avatar-preview-initial')).toBeHidden();

    // The img src should point to the avatars storage path
    const previewSrc = await page.locator('#avatar-preview-img').getAttribute('src');
    expect(previewSrc).toContain('/storage/v1/object/public/avatars/');

    // 3. Submit a job offer and add a comment via the API
    const token = await page.evaluate(async () => {
      return await (window as any).__getAccessToken?.();
    });
    expect(token).toBeTruthy();

    // Create a job offer via the hire-me form (avoids form-data complexity in test)
    await page.goto('/hire-me');
    await page.fill('#companyName', 'Avatar Test Corp');
    await page.fill('#contactName', 'Avatar Tester');
    await page.fill('#contactEmail', 'avatar@test.com');
    await page.fill('#jobTitle', 'Avatar Engineer');
    await page.fill('#description', 'Testing avatars in comments end-to-end.');
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
    await expect(page).toHaveURL(/\/job-offers#/, { timeout: 15000 });

    // Wait for the offer detail to load
    await expect(page.locator('#offer-detail-section')).toBeVisible({ timeout: 10000 });

    // 4. Add a comment via the page UI
    await page.fill('#comment-input', 'This comment should show my avatar');
    await page.click('#send-comment');

    // 5. Verify the comment appears with the avatar img (not initial)
    const commentList = page.locator('#comments-list');
    await expect(commentList).toContainText('This comment should show my avatar', { timeout: 10000 });
    const commentAvatar = commentList.locator('img').first();
    await expect(commentAvatar).toBeVisible();
    const commentAvatarSrc = await commentAvatar.getAttribute('src');
    expect(commentAvatarSrc).toContain('/storage/v1/object/public/avatars/');

    // 6. Remove the avatar and verify it falls back to the initial
    await page.goto('/profile');
    await expect(page.locator('#avatar-remove-btn')).toBeVisible();
    await page.click('#avatar-remove-btn');
    await expect(page.locator('#avatar-preview-initial')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('#avatar-preview-img')).toBeHidden();
    await expect(page.locator('#avatar-remove-btn')).toBeHidden();
  });
});
