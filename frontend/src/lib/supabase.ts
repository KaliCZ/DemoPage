import type { SupabaseClient } from "@supabase/supabase-js";

export const SUPABASE_URL = import.meta.env.PUBLIC_SUPABASE_URL || "";
export const SUPABASE_PUBLISHABLE_KEY = import.meta.env.PUBLIC_SUPABASE_PUBLISHABLE_KEY || "";

let _clientPromise: Promise<SupabaseClient | null> | undefined;
const AUTH_REQUEST_TIMEOUT_MS = 30000;

function fetchWithAuthTimeout(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const url = input instanceof Request ? input.url : input.toString();
  if (!url.startsWith(`${SUPABASE_URL.replace(/\/$/, "")}/auth/v1/`)) return fetch(input, init);

  // Abort the transport so an expired sign-in cannot later persist a session.
  const timeoutSignal = AbortSignal.timeout(AUTH_REQUEST_TIMEOUT_MS);
  const callerSignal = init?.signal ?? (input instanceof Request ? input.signal : undefined);
  return fetch(input, {
    ...init,
    signal: callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal,
  });
}

/** Shared client, or null when the Supabase env vars aren't set. */
export function getSupabaseClient(): Promise<SupabaseClient | null> {
  if (_clientPromise !== undefined) return _clientPromise;
  if (!SUPABASE_URL || !SUPABASE_PUBLISHABLE_KEY) {
    _clientPromise = Promise.resolve(null);
    return _clientPromise;
  }
  // Dynamic so the ~50 KiB SDK stays off the first-paint path; Layout.astro's pre-paint script covers auth UI until then.
  _clientPromise = import("@supabase/supabase-js").then(({ createClient }) =>
    createClient(SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, { global: { fetch: fetchWithAuthTimeout } }),
  );
  return _clientPromise;
}

export const API_URL = import.meta.env.PUBLIC_API_URL || "";
