/** Sentry browser SDK — initialised once from the Sentry.astro component. */

import * as Sentry from "@sentry/browser";
import { browserTracingIntegration } from "@sentry/browser";

let initialised = false;

export function initSentry(dsn: string): void {
  if (initialised) return;
  initialised = true;

  Sentry.init({
    dsn,
    environment: import.meta.env.PROD ? "production" : "development",
    tracesSampleRate: 1.0,
    integrations: [browserTracingIntegration()],
    tracePropagationTargets: ["localhost", /^\/api\//, "https://api.kalandra.tech"],
  });
}

/** Tag Sentry events with the current user. Call on auth state changes. */
export function setSentryUser(user: { id: string; email?: string; user_metadata?: Record<string, any> } | null): void {
  if (!user) {
    Sentry.setUser(null);
    return;
  }

  Sentry.setUser({
    id: user.id,
    email: user.email ?? undefined,
    username: user.user_metadata?.full_name ?? user.email?.split("@")[0] ?? undefined,
  });
}
