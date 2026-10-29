/** Client-minted id that keys a reader's views and reactions before they sign in. */
import { getAccessToken, getCurrentUser } from "./auth";

const VISITOR_ID_KEY = "kalandra_visitor_id";
const LINKED_MARKER_PREFIX = "kalandra_visitor_linked:";

// Fallback only for blocked localStorage; storage is read on each call so separately-bundled islands see a rotation.
let cachedVisitorId: string | null = null;

export function getVisitorId(): string {
  try {
    let id = localStorage.getItem(VISITOR_ID_KEY);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(VISITOR_ID_KEY, id);
    }
    cachedVisitorId = id;
    return id;
  } catch {
    if (!cachedVisitorId) cachedVisitorId = crypto.randomUUID();
    return cachedVisitorId;
  }
}

export function resetVisitorId(): string {
  const id = crypto.randomUUID();
  try {
    localStorage.setItem(VISITOR_ID_KEY, id);
  } catch {
    // No storage — the in-memory fallback still hands the tab a fresh id.
  }
  cachedVisitorId = id;
  return id;
}

export function visitorHeaders(token: string | null): Record<string, string> {
  const headers: Record<string, string> = { "X-Visitor-Id": getVisitorId() };
  if (token) headers.Authorization = `Bearer ${token}`;
  return headers;
}

/** Folds the current browser's anonymous views and reactions into the signed-in account, once per user. */
export async function ensureVisitorLinked(apiUrl: string): Promise<void> {
  const user = await getCurrentUser();
  if (!user?.id) return;

  const visitorId = getVisitorId();
  const marker = LINKED_MARKER_PREFIX + user.id;
  try {
    if (localStorage.getItem(marker) === visitorId) return;
  } catch {
    // No persistence to dedupe on — the request below is idempotent, so a repeat is harmless.
  }

  try {
    const token = await getAccessToken();
    if (!token) return;
    const res = await fetch(`${apiUrl}/api/blog/visitor/link`, {
      method: "POST",
      headers: visitorHeaders(token),
    });
    if (res.ok) {
      try {
        localStorage.setItem(marker, visitorId);
      } catch {
        // Attribution still happened server-side; we just can't remember it locally.
      }
    }
  } catch {
    // Attribution is best-effort — the next sign-in retries.
  }
}

// Rotate on sign-out so a shared computer's later activity isn't attributed to the account that left; registered at
// module load so it runs before the islands' own auth-change handlers reload their data.
if (typeof window !== "undefined" && !(window as any).__blogVisitorRotationWired) {
  (window as any).__blogVisitorRotationWired = true;
  let wasSignedIn = false;
  window.addEventListener("auth-change", (event) => {
    const signedIn = !!(event as CustomEvent).detail?.user;
    if (wasSignedIn && !signedIn) resetVisitorId();
    wasSignedIn = signedIn;
  });
}
