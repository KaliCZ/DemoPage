/** Thin wrapper around the BetterStack browser SDK globals. */

type BetterStackFn = (...args: unknown[]) => void;

function bs(): BetterStackFn | undefined {
  return (window as any).betterstack as BetterStackFn | undefined;
}

/** Identify the current user. Call on sign-in and auth state changes. */
export function identifyUser(user: { id: string; email?: string; user_metadata?: Record<string, any> } | null): void {
  if (!user) return;
  bs()?.("user", {
    id: user.id,
    email: user.email ?? "",
    username: user.user_metadata?.full_name ?? user.email?.split("@")[0] ?? "",
  });
}

/** Track a named event with optional metadata. */
export function track(event: string, data?: Record<string, unknown>): void {
  bs()?.("track", event, data ?? {});
}
