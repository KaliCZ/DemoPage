// Sentry browser SDK init + thin abstraction for identifyUser / track.
//
// Configured via env at build time:
//   - PUBLIC_SENTRY_DSN          → required for any of this to run. Committed in frontend/.env.
//   - PUBLIC_SENTRY_ENV          → optional override for Sentry's `environment` tag. Defaults to
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
const environment = import.meta.env.PUBLIC_SENTRY_ENV || import.meta.env.MODE;

// Resolves to the loaded SDK once init completes; null when no DSN is configured.
// Calls below chain off this promise so anything fired before the SDK arrives still lands.
const sentryReady: Promise<typeof import("@sentry/browser")> | null = sentryDsn
  ? import("@sentry/browser").then((Sentry) => {
      Sentry.init({
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
          const user = Sentry.getCurrentScope().getUser();
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
        // Enables Sentry's Logs product — required for `Sentry.logger.*` and `consoleLoggingIntegration`.
        enableLogs: true,
        // Session Replay sampling. The SDK only loads at all when PUBLIC_SENTRY_DSN is configured at build
        // time, so anyone seeing replays is opting into the full observability stack — record everything.
        replaysSessionSampleRate: 1.0,
        replaysOnErrorSampleRate: 1.0,
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
          // Session Replay — DOM/network/console recording.
          // Defaults mask every text node and input, which makes replays unreadable. Relax them: passwords
          // (`<input type="password">`) are always masked by Sentry regardless of these flags, so the only
          // way sensitive data leaks is if you put a secret in a regular field. If that ever becomes a
          // concern for a specific element, add `data-sentry-mask` to it (or extend the `mask` option below).
          Sentry.replayIntegration({
            maskAllText: false,
            maskAllInputs: false,
            blockAllMedia: false,
            // Sentry also masks `title`, `placeholder`, `aria-label` by default — that's why every input
            // placeholder in the replay reads as `*****`. None of those carry PII on this app, so clear it.
            maskAttributes: [],
          }),
        ],
      });
      // Deliberate pageview log — fires once per full page load (this is a static Astro site, no SPA nav).
      // Gives a per-pageview entry in the Logs tab with the user/UA/locale context populated by beforeSendLog.
      Sentry.logger.info("pageview", {
        "url.path": window.location.pathname,
        "url.query": window.location.search,
        "http.referer": document.referrer,
      });
      return Sentry;
    })
  : null;

/** Identify the current user. Call on sign-in and on auth state changes. */
export function identifyUser(user: AuthUser | null): void {
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
}

/** Record a named application event with optional metadata. */
export function track(event: string, data?: EventData): void {
  sentryReady?.then((Sentry) => {
    // Breadcrumb: visible on captured issues; structured log: visible in the Logs tab regardless.
    Sentry.addBreadcrumb({ category: "app", message: event, level: "info", data: data ?? {} });
    Sentry.logger.info(event, data ?? {});
  });
}

// Exposed for `define:vars` inline scripts in Astro pages, which can't import ES modules.
(window as any).observability = { identifyUser, track };

declare global {
  interface Window {
    observability?: {
      identifyUser: typeof identifyUser;
      track: typeof track;
    };
  }
}
