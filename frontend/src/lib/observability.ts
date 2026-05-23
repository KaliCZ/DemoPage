// Observability abstraction — picks the configured backend(s) and fans calls out to them.
//
// Configured via env at build time:
//   - PUBLIC_SENTRY_DSN          → Sentry browser SDK (@sentry/browser, dynamic-imported on module load).
//   - PUBLIC_BETTERSTACK_TOKEN   → BetterStack JS tag injected by Observability.astro.
// If both are set, calls go to both. If neither, calls are no-ops.
//
// The Sentry import is dynamic and gated on a build-time env check; Vite tree-shakes the
// entire `@sentry/browser` chunk out of the bundle when PUBLIC_SENTRY_DSN is empty.

type AuthUser = {
  id: string;
  email?: string;
  user_metadata?: Record<string, unknown>;
};

type EventData = Record<string, unknown>;

interface Provider {
  identifyUser(user: AuthUser | null): void;
  track(event: string, data?: EventData): void;
}

const sentryDsn = import.meta.env.PUBLIC_SENTRY_DSN;
const mode = import.meta.env.MODE;

// Resolves to the loaded SDK once init completes; null when no DSN is configured.
// Provider methods chain off this promise so calls made before the SDK arrives still land.
const sentryReady: Promise<typeof import("@sentry/browser")> | null = sentryDsn
  ? import("@sentry/browser").then((Sentry) => {
      Sentry.init({
        dsn: sentryDsn,
        environment: mode,
        tracesSampleRate: 1.0,
        tracePropagationTargets: ["localhost", /^\/api\//, "https://api.kalandra.tech"],
        // Enables Sentry's Logs product — required for `Sentry.logger.*` and `consoleLoggingIntegration`.
        enableLogs: true,
        // Session Replay sampling — picked up by replayIntegration below. Skipped entirely in dev.
        replaysSessionSampleRate: mode === "development" ? 0 : 0.1,
        replaysOnErrorSampleRate: mode === "development" ? 0 : 1.0,
        integrations: (defaults) => [
          ...defaults,
          // v10 dropped browserTracingIntegration from defaults; without it, tracesSampleRate has nothing to sample.
          // Resource loads (JS chunks, CSS, fonts, images) and stray performance.mark/measure spans from
          // dependencies like @supabase/auth-js drown out the actual signal — fetches and Web Vitals —
          // so we silence them. Pageload + navigation transactions and fetch spans still flow normally.
          Sentry.browserTracingIntegration({
            ignoreResourceSpans: ["resource.script", "resource.css", "resource.img", "resource.link", "resource.other"],
            ignorePerformanceApiSpans: [/.*/],
          }),
          // Forward the useful console levels to Sentry Logs. Skipping `debug`/`trace`/`assert` —
          // they're mostly framework/dependency noise that wouldn't help when diagnosing real issues.
          Sentry.consoleLoggingIntegration({ levels: ["log", "info", "warn", "error"] }),
          // Session Replay — DOM/network/console recording. In dev, the Aspire OTLP fetches would create noise
          // and burn quota for no benefit. `maskAllText`/`blockAllMedia` defaults stay on for PII safety.
          ...(mode === "development" ? [] : [Sentry.replayIntegration()]),
        ],
      });
      return Sentry;
    })
  : null;

const sentryProvider: Provider = {
  identifyUser(user) {
    sentryReady?.then((Sentry) =>
      Sentry.setUser(
        user
          ? {
              id: user.id,
              email: user.email ?? "",
              username: (user.user_metadata?.full_name as string | undefined) ?? user.email?.split("@")[0] ?? "",
            }
          : null,
      ),
    );
  },
  track(event, data) {
    sentryReady?.then((Sentry) => {
      // Breadcrumb: visible on captured issues; structured log: visible in the Logs tab regardless.
      Sentry.addBreadcrumb({ category: "app", message: event, level: "info", data: data ?? {} });
      Sentry.logger.info(event, data ?? {});
    });
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
};

const providers: Provider[] = [];
if (sentryDsn) providers.push(sentryProvider);
if (import.meta.env.PUBLIC_BETTERSTACK_TOKEN) providers.push(betterStackProvider);

/** Identify the current user. Call on sign-in and on auth state changes. */
export function identifyUser(user: AuthUser | null): void {
  for (const p of providers) p.identifyUser(user);
}

/** Record a named application event with optional metadata. */
export function track(event: string, data?: EventData): void {
  for (const p of providers) p.track(event, data);
}

// Exposed for `define:vars` inline scripts in Astro pages, which can't import ES modules.
(window as any).__obs = { identifyUser, track };

declare global {
  interface Window {
    __obs?: {
      identifyUser: typeof identifyUser;
      track: typeof track;
    };
  }
}
