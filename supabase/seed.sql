-- Well-known admin user for local development: dev@kalandra.local / Heslo123.
-- Runs on the first `supabase start` and on `supabase db reset`.

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
    '{"full_name":"Admin User"}',
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

-- job-offer-attachments RLS: a path's first segment is the owner's user id ({user_id}/{job_offer_id}/...).

DO $$
BEGIN
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

-- avatars RLS: a path's first segment is the owner's user id ({user_id}/avatar).
-- The bucket is public, but upsert still needs SELECT; scoping it to the user's folder stops listing others' files.

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies WHERE policyname = 'Users can read own avatar'
  ) THEN
    CREATE POLICY "Users can read own avatar"
      ON storage.objects FOR SELECT
      TO authenticated
      USING (
        bucket_id = 'avatars'
        AND (storage.foldername(name))[1] = auth.uid()::text
      );
  END IF;

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
END $$;
