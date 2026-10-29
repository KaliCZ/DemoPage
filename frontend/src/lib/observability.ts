type AuthUser = {
  id: string;
  email?: string;
  user_metadata?: Record<string, unknown>;
};

type EventData = Record<string, unknown>;

const sentryDsn = import.meta.env.PUBLIC_SENTRY_DSN;
const environment = import.meta.env.PUBLIC_SENTRY_ENVIRONMENT || import.meta.env.MODE;

// Destructuring only the functions we use, not the `Sentry` namespace, lets the bundler tree-shake Session Replay (rrweb).
type SentryApi = Pick<typeof import("@sentry/browser"), "setUser" | "addBreadcrumb" | "logger" | "captureException">;
// Dynamic so a build without a DSN drops the SDK, but not deferred: init must run before the client:idle islands' first API calls.
const sentryReady: Promise<SentryApi> | null = sentryDsn
  ? import("@sentry/browser").then(
      ({
        init,
        browserTracingIntegration,
        consoleLoggingIntegration,
        getCurrentScope,
        setUser,
        addBreadcrumb,
        logger,
        captureException,
      }) => {
        init({
          dsn: sentryDsn,
          environment,
          tracesSampleRate: 1.0,
          tracePropagationTargets: ["localhost", /^\/api\//, "https://api.kalandra.tech"],
          // So Sentry attaches the client IP to events and logs.
          sendDefaultPii: true,
          // Sentry Logs don't inherit the scope's user/UA/locale context the way events do, so copy it into the attributes.
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
          enableLogs: true,
          integrations: (defaults) => [
            ...defaults,
            // Resource and performance.mark/measure spans (e.g. from @supabase/auth-js) drown out fetches and Web Vitals.
            browserTracingIntegration({
              ignoreResourceSpans: ["resource.script", "resource.css", "resource.img", "resource.link", "resource.other"],
              ignorePerformanceApiSpans: [/.*/],
            }),
            // debug, trace and assert are mostly framework and dependency noise.
            consoleLoggingIntegration({ levels: ["log", "info", "warn", "error"] }),
          ],
        });
        // Once per pageview, since every navigation on this static site is a full page load.
        logger.info("pageview", {
          "url.path": window.location.pathname,
          "url.query": window.location.search,
          "http.referer": document.referrer,
        });
        return { setUser, addBreadcrumb, logger, captureException };
      },
    )
  : null;

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

export function track(event: string, data?: EventData): void {
  sentryReady?.then((sentry) => {
    // Breadcrumb: visible on captured issues; structured log: visible in the Logs tab regardless.
    sentry.addBreadcrumb({ category: "app", message: event, level: "info", data: data ?? {} });
    sentry.logger.info(event, data ?? {});
  });
}

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
