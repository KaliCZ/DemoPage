/**
 * Supabase client configuration.
 *
 * These values are public (safe to expose in the browser).
 * Defaults for local Supabase are in frontend/.env (committed).
 * Override with frontend/.env.local (gitignored) for custom values.
 *
 * The heavy @supabase/supabase-js bundle (~50 KiB) is loaded via dynamic
 * import() so it stays out of the initial module graph and doesn't block
 * first paint. The inline <head> script in Layout.astro already provides
 * auth UI state from localStorage, so the page renders correctly before
 * this module finishes loading.
 */
import type { SupabaseClient } from "@supabase/supabase-js";

export const SUPABASE_URL = import.meta.env.PUBLIC_SUPABASE_URL || "";
export const SUPABASE_PUBLISHABLE_KEY = import.meta.env.PUBLIC_SUPABASE_PUBLISHABLE_KEY || "";

let _clientPromise: Promise<SupabaseClient | null> | undefined;

/**
 * Returns a shared browser-side Supabase client, or null if env vars are missing.
 * Singleton — safe to import from multiple components; only one client is created.
 * The first call triggers a dynamic import of @supabase/supabase-js.
 */
export function getSupabaseClient(): Promise<SupabaseClient | null> {
  if (_clientPromise !== undefined) return _clientPromise;
  if (!SUPABASE_URL || !SUPABASE_PUBLISHABLE_KEY) {
    _clientPromise = Promise.resolve(null);
    return _clientPromise;
  }
  _clientPromise = import("@supabase/supabase-js").then(({ createClient }) => createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY));
  return _clientPromise;
}

/**
 * API base URL for the backend.
 * In production: https://api.kalandra.tech
 * In development: http://localhost:5000
 */
export const API_URL = import.meta.env.PUBLIC_API_URL || "";
