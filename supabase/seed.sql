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
    '{"provider":"email","providers":["email"],"roles":["admin"]}',
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

-- Storage RLS policies for job-offer-attachments bucket.
-- Files are stored at: {user_id}/{job_offer_id}/{filename}
-- Authenticated users can upload to their own folder and read their own files.
-- Note: admin access is handled by the backend via service role key or signed URLs.

DO $$
BEGIN
  -- Allow authenticated users to upload files under their own user ID prefix
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can upload own attachments'
  ) THEN
    CREATE POLICY "Users can upload own attachments"
      ON storage.objects FOR INSERT
      TO authenticated
      WITH CHECK (
        bucket_id = 'job-offer-attachments'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;

  -- Allow authenticated users to read their own uploaded files
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can read own attachments'
  ) THEN
    CREATE POLICY "Users can read own attachments"
      ON storage.objects FOR SELECT
      TO authenticated
      USING (
        bucket_id = 'job-offer-attachments'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;
END $$;

-- Storage RLS policies for avatars bucket.
-- Files are stored at: {user_id}/avatar.{ext}
-- Authenticated users can upload/update/delete their own avatar.
-- Public read access (bucket is public).

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can upload own avatar'
  ) THEN
    CREATE POLICY "Users can upload own avatar"
      ON storage.objects FOR INSERT
      TO authenticated
      WITH CHECK (
        bucket_id = 'avatars'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can update own avatar'
  ) THEN
    CREATE POLICY "Users can update own avatar"
      ON storage.objects FOR UPDATE
      TO authenticated
      USING (
        bucket_id = 'avatars'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can delete own avatar'
  ) THEN
    CREATE POLICY "Users can delete own avatar"
      ON storage.objects FOR DELETE
      TO authenticated
      USING (
        bucket_id = 'avatars'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Anyone can read avatars'
  ) THEN
    CREATE POLICY "Anyone can read avatars"
      ON storage.objects FOR SELECT
      TO public
      USING (bucket_id = 'avatars');
  END IF;
END $$;
