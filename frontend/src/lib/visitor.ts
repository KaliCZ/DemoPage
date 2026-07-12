/**
 * The anonymous visitor id: a client-minted UUID kept in localStorage that keys a
 * reader's views and reactions before they sign in. On sign-in it is linked to the
 * account so prior anonymous activity is attributed (see the backend LinkVisitor); on
 * sign-out it is rotated so the next anonymous session is a clean, unlinked identity.
 */
import { getAccessToken, getCurrentUser } from "./auth";

const VISITOR_ID_KEY = "kalandra_visitor_id";
const LINKED_MARKER_PREFIX = "kalandra_visitor_linked:";

// Only a fallback for when localStorage is blocked (private mode); normally we read storage
// each call so a rotation is seen even across separately-bundled island modules.
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

// A fresh anonymous session on sign-out: later activity on a shared computer can't be
// attributed to the account that just signed out.
function rotateVisitorId(): void {
  const id = crypto.randomUUID();
  try {
    localStorage.setItem(VISITOR_ID_KEY, id);
  } catch {
    // No storage — the in-memory fallback still hands the tab a fresh id.
  }
  cachedVisitorId = id;
}

/** Adds the visitor header (and, when signed in, the bearer token) to a blog API request. */
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

// Rotate on sign-out. Wired once at module load (guarded) — this runs before any island's
// own auth-change handler, so islands read the fresh id when they reload their data.
if (typeof window !== "undefined" && !(window as any).__blogVisitorRotationWired) {
  (window as any).__blogVisitorRotationWired = true;
  let wasSignedIn = false;
  window.addEventListener("auth-change", (event) => {
    const signedIn = !!(event as CustomEvent).detail?.user;
    if (wasSignedIn && !signedIn) rotateVisitorId();
    wasSignedIn = signedIn;
  });
}
