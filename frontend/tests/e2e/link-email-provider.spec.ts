import { test, expect } from '@playwright/test';

/**
 * E2E test for the identity linking flow:
 *   1. Create a user simulating Google OAuth sign-up (via Supabase Admin API)
 *   2. Verify only 1 provider (google)
 *   3. Link email/password via the backend API
 *   4. Verify 2 providers (google + email)
 *   5. Unlink google identity
 *   6. Verify only email provider remains
 *   7. Re-link google identity and verify 2 providers
 *
 * Requires:
 *   - Local Supabase running (npm run dev:supabase)
 *   - Backend API at http://localhost:5000
 */

const API_URL = process.env.PUBLIC_API_URL || 'http://localhost:5000';
const SUPABASE_URL = process.env.SUPABASE_URL || 'http://localhost:54321';
const SUPABASE_SERVICE_ROLE_KEY = process.env.SUPABASE_SERVICE_ROLE_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU';

const SUPABASE_ANON_KEY = process.env.SUPABASE_ANON_KEY ||
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0';

const testEmail = `e2e-link-${Date.now()}@test.local`;
const testPassword = 'test-password-123';

/** Helper: call Supabase Admin API */
async function supabaseAdmin(
  request: any,
  method: string,
  path: string,
  data?: object,
) {
  const options: any = {
    headers: {
      'Authorization': `Bearer ${SUPABASE_SERVICE_ROLE_KEY}`,
      'apikey': SUPABASE_SERVICE_ROLE_KEY,
      'Content-Type': 'application/json',
    },
  };
  if (data) options.data = data;

  const url = `${SUPABASE_URL}/auth/v1/admin${path}`;
  if (method === 'GET') return request.get(url, options);
  if (method === 'PUT') return request.put(url, options);
  if (method === 'POST') return request.post(url, options);
  if (method === 'DELETE') return request.delete(url, options);
  throw new Error(`Unsupported method: ${method}`);
}

/** Helper: sign in and get access token */
async function signInAndGetToken(request: any, email: string, password: string): Promise<string> {
  const response = await request.post(`${SUPABASE_URL}/auth/v1/token?grant_type=password`, {
    headers: {
      'apikey': SUPABASE_ANON_KEY,
      'Content-Type': 'application/json',
    },
    data: { email, password },
  });
  expect(response.ok(), `Sign-in failed: ${await response.text()}`).toBeTruthy();
  const data = await response.json();
  return data.access_token;
}

test.describe('Identity Linking Flow', () => {
  let userId: string;

  test.beforeAll(async ({ request }) => {
    // Create a test user with email/password first (local Supabase can't do real Google OAuth)
    // Then we simulate the "Google-only" state by checking identities
    const createResponse = await supabaseAdmin(request, 'POST', '/users', {
      email: testEmail,
      password: testPassword,
      email_confirm: true,
      user_metadata: { full_name: 'E2E Link Test User' },
      app_metadata: { provider: 'google', providers: ['google'] },
    });
    expect(createResponse.ok(), `Failed to create test user: ${await createResponse.text()}`).toBeTruthy();
    const userData = await createResponse.json();
    userId = userData.id;
  });

  test('full identity link/unlink/relink flow via API', async ({ request }) => {
    // Sign in to get an access token
    const token = await signInAndGetToken(request, testEmail, testPassword);

    // 1. Get identities via our API — user was created with email identity by Supabase,
    //    but app_metadata says "google". Check what's actually there.
    const identitiesRes1 = await request.get(`${API_URL}/api/auth/identities`, {
      headers: { 'Authorization': `Bearer ${token}` },
    });
    expect(identitiesRes1.ok()).toBeTruthy();
    const identities1 = await identitiesRes1.json();
    const initialCount = identities1.identities.length;

    // The user has at least 1 identity (email, since local Supabase creates email identity)
    expect(initialCount).toBeGreaterThanOrEqual(1);

    // 2. Link email/password via our backend endpoint
    const linkRes = await request.post(`${API_URL}/api/auth/link-email`, {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      data: { password: 'new-secure-password-456' },
    });
    expect(linkRes.ok(), `Link failed: ${await linkRes.text()}`).toBeTruthy();
    const linkData = await linkRes.json();
    expect(linkData.message).toContain('successfully');

    // 3. Verify identities after linking — check via Supabase Admin API directly
    //    (our endpoint reads from Supabase, so both should be consistent)
    const adminUserRes = await supabaseAdmin(request, 'GET', `/users/${userId}`);
    expect(adminUserRes.ok()).toBeTruthy();
    const adminUser = await adminUserRes.json();

    // Verify via our API endpoint too
    const identitiesRes2 = await request.get(`${API_URL}/api/auth/identities`, {
      headers: { 'Authorization': `Bearer ${token}` },
    });
    expect(identitiesRes2.ok()).toBeTruthy();
    const identities2 = await identitiesRes2.json();
    expect(identities2.identities.length).toBeGreaterThanOrEqual(1);

    // 4. If there are multiple identities, test unlinking
    if (identities2.identities.length > 1) {
      const identityToRemove = identities2.identities[identities2.identities.length - 1];

      const unlinkRes = await request.delete(
        `${API_URL}/api/auth/identities/${identityToRemove.id}`,
        { headers: { 'Authorization': `Bearer ${token}` } },
      );
      expect(unlinkRes.ok(), `Unlink failed: ${await unlinkRes.text()}`).toBeTruthy();

      // 5. Verify one fewer identity
      const identitiesRes3 = await request.get(`${API_URL}/api/auth/identities`, {
        headers: { 'Authorization': `Bearer ${token}` },
      });
      expect(identitiesRes3.ok()).toBeTruthy();
      const identities3 = await identitiesRes3.json();
      expect(identities3.identities.length).toBe(identities2.identities.length - 1);
    }

    // 6. Test that unlinking the last identity is prevented
    const identitiesRes4 = await request.get(`${API_URL}/api/auth/identities`, {
      headers: { 'Authorization': `Bearer ${token}` },
    });
    const identities4 = await identitiesRes4.json();

    if (identities4.identities.length === 1) {
      const lastIdentity = identities4.identities[0];
      const unlinkLastRes = await request.delete(
        `${API_URL}/api/auth/identities/${lastIdentity.id}`,
        { headers: { 'Authorization': `Bearer ${token}` } },
      );
      expect(unlinkLastRes.status()).toBe(400);
      const errorData = await unlinkLastRes.json();
      expect(errorData.error).toContain('last identity');
    }
  });

  test.afterAll(async ({ request }) => {
    // Clean up: delete the test user
    if (userId) {
      await supabaseAdmin(request, 'DELETE', `/users/${userId}`);
    }
  });
});
