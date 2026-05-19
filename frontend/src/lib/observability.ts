// Observability abstraction — picks the configured backend(s) and fans calls out to them.
//
// Configured via env at build time:
//   - PUBLIC_SENTRY_DSN          → Sentry browser SDK (loader script from sentry-cdn).
//   - PUBLIC_BETTERSTACK_TOKEN   → BetterStack JS tag.
// If both are set, calls go to both. If neither, calls are no-ops.

type AuthUser = {
  id: string;
  email?: string;
  user_metadata?: Record<string, unknown>;
};

type EventData = Record<string, unknown>;

interface Provider {
  identifyUser(user: AuthUser | null): void;
  track(event: string, data?: EventData): void;
  captureException(err: unknown, context?: EventData): void;
}

// The Sentry loader script (https://js.sentry-cdn.com/<key>.min.js) exposes a `window.Sentry`
// proxy that queues calls until the real SDK finishes loading, so we can dispatch eagerly.
const sentryProvider: Provider = {
  identifyUser(user) {
    const s = (window as any).Sentry;
    if (!s) return;
    if (!user) {
      s.setUser?.(null);
      return;
    }
    s.setUser?.({
      id: user.id,
      email: user.email ?? "",
      username: (user.user_metadata?.full_name as string | undefined) ?? user.email?.split("@")[0] ?? "",
    });
  },
  track(event, data) {
    (window as any).Sentry?.addBreadcrumb?.({
      category: "app",
      message: event,
      level: "info",
      data: data ?? {},
    });
  },
  captureException(err, context) {
    (window as any).Sentry?.captureException?.(err, context ? { extra: context } : undefined);
  },
};

const betterStackProvider: Provider = {
  identifyUser(user) {
    if (!user) return;
    (window as any).betterstack?.("user", {
      id: user.id,
      email: user.email ?? "",
      username: (user.user_metadata?.full_name as string | undefined) ?? user.email?.split("@")[0] ?? "",
    });
  },
  track(event, data) {
    (window as any).betterstack?.("track", event, data ?? {});
  },
  captureException() {
    // BetterStack auto-captures uncaught errors via its script — no explicit API.
  },
};

const providers: Provider[] = [];
if (import.meta.env.PUBLIC_SENTRY_DSN) providers.push(sentryProvider);
if (import.meta.env.PUBLIC_BETTERSTACK_TOKEN) providers.push(betterStackProvider);

/** Identify the current user. Call on sign-in and on auth state changes. */
export function identifyUser(user: AuthUser | null): void {
  for (const p of providers) p.identifyUser(user);
}

/** Record a named application event with optional metadata. */
export function track(event: string, data?: EventData): void {
  for (const p of providers) p.track(event, data);
}

/** Explicitly capture an exception. Uncaught errors are reported automatically. */
export function captureException(err: unknown, context?: EventData): void {
  for (const p of providers) p.captureException(err, context);
}

// Exposed for `define:vars` inline scripts in Astro pages, which can't import ES modules.
(window as any).__obs = { identifyUser, track, captureException };

declare global {
  interface Window {
    __obs?: {
      identifyUser: typeof identifyUser;
      track: typeof track;
      captureException: typeof captureException;
    };
  }
}
