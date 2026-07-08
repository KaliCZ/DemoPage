/** Typed access to the auth globals Layout.astro exposes for pages and islands. */

export async function getAccessToken(): Promise<string | null> {
  return ((await (window as any).__getAccessToken?.()) as string | null) ?? null;
}

export async function getCurrentUser(): Promise<any | null> {
  return ((await (window as any).__getUser?.()) as any | null) ?? null;
}

export function openAuthDialog(): void {
  (window as any).__openAuthDialog?.();
}

export function userHasAdminRole(user: any): boolean {
  return Array.isArray(user?.app_metadata?.roles) && user.app_metadata.roles.includes("admin");
}

export interface AuthGateOptions {
  /** Spinner shown until the first auth check completes. */
  loadingId: string;
  /** Login prompt / access-denied section shown when the visitor doesn't qualify. */
  blockedId: string;
  /** Page content shown when the visitor qualifies. */
  contentId: string;
  requireAdmin?: boolean;
}

/**
 * Toggles the loading / blocked / content sections of an auth-gated page on
 * every `auth-change`. Pre-paint visibility is handled separately by the
 * `signed-in-only` / `signed-out-only` CSS tiers in Layout.astro.
 */
export function wireAuthGate({ loadingId, blockedId, contentId, requireAdmin = false }: AuthGateOptions): void {
  window.addEventListener("auth-change", (event) => {
    const user = (event as CustomEvent).detail?.user;
    const allowed = requireAdmin ? userHasAdminRole(user) : !!user;
    document.getElementById(loadingId)?.classList.add("hidden");
    document.getElementById(blockedId)?.classList.toggle("hidden", allowed);
    document.getElementById(contentId)?.classList.toggle("hidden", !allowed);
  });
}
