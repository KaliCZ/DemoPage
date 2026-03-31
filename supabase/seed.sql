-- Seed a well-known dev user for local development and E2E tests.
-- Credentials: dev@kalandra.local / devpass123
-- This runs on `supabase start` (first time) and `supabase db reset`.

DO $$
DECLARE
  v_user_id  UUID := '11111111-1111-1111-1111-111111111111';
  v_email    TEXT := 'dev@kalandra.local';
  v_password TEXT := crypt('Heslo123', gen_salt('bf'));
BEGIN
  INSERT INTO auth.users (
    id,
    instance_id,
    aud,
    role,
    email,
    encrypted_password,
    email_confirmed_at,
    raw_app_meta_data,
    raw_user_meta_data,
    confirmation_token,
    recovery_token,
    email_change_token_new,
    email_change_token_current,
    email_change,
    phone,
    phone_change,
    phone_change_token,
    reauthentication_token,
    is_sso_user,
    is_anonymous,
    created_at,
    updated_at
  ) VALUES (
    v_user_id,
    '00000000-0000-0000-0000-000000000000',
    'authenticated',
    'authenticated',
    v_email,
    v_password,
    NOW(),
    '{"provider":"email","providers":["email"],"role":"admin"}',
    '{"full_name":"Dev User"}',
    '',
    '',
    '',
    '',
    '',
    '',
    '',
    '',
    '',
    FALSE,
    FALSE,
    NOW(),
    NOW()
  )
  ON CONFLICT (id) DO NOTHING;

  INSERT INTO auth.identities (
    id,
    user_id,
    identity_data,
    provider,
    provider_id,
    last_sign_in_at,
    created_at,
    updated_at
  ) VALUES (
    v_user_id,
    v_user_id,
    format('{"sub":"%s","email":"%s"}', v_user_id, v_email)::jsonb,
    'email',
    v_user_id,
    NOW(),
    NOW(),
    NOW()
  )
  ON CONFLICT DO NOTHING;
END $$;
