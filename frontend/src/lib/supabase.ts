/**
 * Supabase client configuration.
 *
 * These values are public (safe to expose in the browser).
 * Defaults for local Supabase are in frontend/.env (committed).
 * Override with frontend/.env.local (gitignored) for custom values.
 */
import { createClient, type SupabaseClient } from "@supabase/supabase-js";

export const SUPABASE_URL = import.meta.env.PUBLIC_SUPABASE_URL || "";
export const SUPABASE_PUBLISHABLE_KEY = import.meta.env.PUBLIC_SUPABASE_PUBLISHABLE_KEY || "";

let _client: SupabaseClient | null | undefined;

/**
 * Returns a shared browser-side Supabase client, or null if env vars are missing.
 * Singleton — safe to import from multiple components; only one client is created.
 */
export function getSupabaseClient(): SupabaseClient | null {
  if (_client !== undefined) return _client;
  _client = SUPABASE_URL && SUPABASE_PUBLISHABLE_KEY ? createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY) : null;
  return _client;
}

/**
 * API base URL for the backend.
 * In production: https://api.kalandra.tech
 * In development: http://localhost:5000
 */
export const API_URL = import.meta.env.PUBLIC_API_URL || "";
