// Sentry browser SDK init + thin abstraction for identifyUser / track.
//
// Configured via env at build time:
//   - PUBLIC_SENTRY_DSN          → required for any of this to run. Committed in frontend/.env.
//   - PUBLIC_SENTRY_ENVIRONMENT  → optional override for Sentry's `environment` tag. Defaults to
//                                  Vite's MODE ("development" in `astro dev`, "production" in builds).
//                                  CI test jobs pass "ci" so their events filter out of prod views.
//
// The Sentry import is dynamic and gated on PUBLIC_SENTRY_DSN at build time, so Vite tree-shakes the
// entire `@sentry/browser` chunk out of the bundle when the DSN is empty.

type AuthUser = {
  id: string;
  email?: string;
  user_metadata?: Record<string, unknown>;
};

type EventData = Record<string, unknown>;

const sentryDsn = import.meta.env.PUBLIC_SENTRY_DSN;
const environment = import.meta.env.PUBLIC_SENTRY_ENVIRONMENT || import.meta.env.MODE;

// Resolves to a minimal SDK surface once init completes; null when no DSN is configured. Calls below
// chain off this promise so anything fired before the SDK arrives still lands. Pulling only the
// functions we use off the module by name (rather than the whole `Sentry` namespace) is what lets the
// bundler tree-shake Session Replay (rrweb) — the SDK's heaviest feature — out of the chunk entirely.
type SentryApi = Pick<typeof import("@sentry/browser"), "setUser" | "addBreadcrumb" | "logger">;
// Import eagerly (this module runs in <head>) rather than deferring, so init wins the race against the
// client:idle islands: their API calls are only traced, and early errors only captured, once init runs.
const sentryReady: Promise<SentryApi> | null = sentryDsn
  ? import("@sentry/browser").then(
      ({ init, browserTracingIntegration, consoleLoggingIntegration, getCurrentScope, setUser, addBreadcrumb, logger }) => {
        init({
          dsn: sentryDsn,
          environment,
          tracesSampleRate: 1.0,
          tracePropagationTargets: ["localhost", /^\/api\//, "https://api.kalandra.tech"],
          // Lets Sentry's ingest server attach the client IP (derived from the connection) to events and logs.
          // Browser/OS/locale already ride along via the default httpContext and cultureContext integrations.
          sendDefaultPii: true,
          // The Logs product doesn't auto-inherit the user/UA/culture context the way events do — it only
          // carries what's in `log.attributes`. Enrich every log with the current scope's user plus a few
          // client-side fields so Sentry's UI populates the User panel and lets us search by user.email,
          // user_agent.original, etc. OTel semantic conventions used where applicable.
          beforeSendLog(log) {
            const user = getCurrentScope().getUser();
            log.attributes = {
              ...log.attributes,
              ...(user?.id !== undefined && { "user.id": String(user.id) }),
              ...(user?.email && { "user.email": user.email }),
              ...(user?.username && { "user.name": user.username }),
              "user_agent.original": navigator.userAgent,
              "browser.language": navigator.language,
              "browser.timezone": Intl.DateTimeFormat().resolvedOptions().timeZone,
              "url.path": window.location.pathname,
            };
            return log;
          },
          // Enables Sentry's Logs product — required for `logger.*` and `consoleLoggingIntegration`.
          enableLogs: true,
          integrations: (defaults) => [
            ...defaults,
            // v10 dropped browserTracingIntegration from defaults; without it, tracesSampleRate has nothing to sample.
            // Resource loads (JS chunks, CSS, fonts, images) and stray performance.mark/measure spans from
            // dependencies like @supabase/auth-js drown out the actual signal — fetches and Web Vitals —
            // so we silence them. Pageload + navigation transactions and fetch spans still flow normally.
            browserTracingIntegration({
              ignoreResourceSpans: ["resource.script", "resource.css", "resource.img", "resource.link", "resource.other"],
              ignorePerformanceApiSpans: [/.*/],
            }),
            // Forward the useful console levels to Sentry Logs. Skipping `debug`/`trace`/`assert` —
            // they're mostly framework/dependency noise that wouldn't help when diagnosing real issues.
            consoleLoggingIntegration({ levels: ["log", "info", "warn", "error"] }),
          ],
        });
        // Deliberate pageview log — fires once per full page load (this is a static Astro site, no SPA nav).
        // Gives a per-pageview entry in the Logs tab with the user/UA/locale context populated by beforeSendLog.
        logger.info("pageview", {
          "url.path": window.location.pathname,
          "url.query": window.location.search,
          "http.referer": document.referrer,
        });
        return { setUser, addBreadcrumb, logger };
      },
    )
  : null;

/** Identify the current user. Call on sign-in and on auth state changes. */
export function identifyUser(user: AuthUser | null): void {
  sentryReady?.then((sentry) =>
    sentry.setUser(
      user
        ? {
            id: user.id,
            email: user.email ?? "",
            username: (user.user_metadata?.full_name as string | undefined) ?? user.email?.split("@")[0] ?? "",
          }
        : null,
    ),
  );
}

/** Record a named application event with optional metadata. */
export function track(event: string, data?: EventData): void {
  sentryReady?.then((sentry) => {
    // Breadcrumb: visible on captured issues; structured log: visible in the Logs tab regardless.
    sentry.addBreadcrumb({ category: "app", message: event, level: "info", data: data ?? {} });
    sentry.logger.info(event, data ?? {});
  });
}

/** Capture an unexpected error as a Sentry issue, with optional context shown on its `extra` panel. */
export function captureException(error: unknown, context?: EventData): void {
  sentryReady?.then((Sentry) => Sentry.captureException(error, context ? { extra: context } : undefined));
}

// Exposed for `define:vars` inline scripts in Astro pages, which can't import ES modules.
(window as any).observability = { identifyUser, track, captureException };

declare global {
  interface Window {
    observability?: {
      identifyUser: typeof identifyUser;
      track: typeof track;
      captureException: typeof captureException;
    };
  }
}
