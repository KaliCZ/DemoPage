// Reusable Cloudflare Turnstile helper.
//
// Two supported integration modes:
//
// 1. Persistent challenge (`execution: "render"`, the default) — for pages
//    where a token is always needed. Challenge runs on render, token is
//    available via `getToken()`. Re-run with `reset()`.
//
//        const ts = createTurnstile('#c');
//        await whenTurnstileReady();
//        ts.render();                    // challenge runs now
//        const token = ts.getToken();    // may be "" if still running
//
// 2. Per-submission challenge (`execution: "execute"`) — for short-lived
//    forms like a sign-in dialog where we want a fresh token AT SUBMIT
//    time, not at render time. Avoids stale-token issues when the form
//    sits idle or when a password manager fills the inputs between
//    render and click.
//
//        ts.render({ execution: "execute", appearance: "interaction-only" });
//        // ... on submit:
//        ts.execute();
//        const token = await ts.waitForToken(15000);
//
// Plus a localStorage-based escalation counter so forms can switch the widget
// to a visible checkbox ('always') after repeated failures per key (e.g. email).

declare global {
  interface Window {
    turnstile?: {
      render: (selector: string | HTMLElement, opts: Record<string, unknown>) => string;
      remove: (id: string) => void;
      reset: (id: string) => void;
      execute: (id: string) => void;
      getResponse: (id: string) => string | undefined;
    };
  }
}

export type TurnstileAppearance = "interaction-only" | "always" | "execute";

const SITE_KEY = import.meta.env.PUBLIC_TURNSTILE_SITE_KEY as string | undefined;

export function turnstileEnabled(): boolean {
  return !!SITE_KEY;
}

const TURNSTILE_SCRIPT_SRC = "https://challenges.cloudflare.com/turnstile/v0/api.js";

// Load Cloudflare's Turnstile API on first need rather than on every page. Idempotent: bails if the
// API is already present or a matching tag is already in the document (a page that needs the widget
// at load, like hire-me, injects its own).
function loadTurnstileScript(): void {
  if (!SITE_KEY || window.turnstile) return;
  if (document.querySelector(`script[src^="${TURNSTILE_SCRIPT_SRC}"]`)) return;
  const script = document.createElement("script");
  script.src = TURNSTILE_SCRIPT_SRC;
  script.async = true;
  script.defer = true;
  document.head.appendChild(script);
}

export function whenTurnstileReady(): Promise<void> {
  return new Promise((resolve) => {
    if (window.turnstile) return resolve();
    loadTurnstileScript();
    const iv = setInterval(() => {
      if (window.turnstile) {
        clearInterval(iv);
        resolve();
      }
    }, 100);
  });
}

/**
   Execution mode — maps directly to Cloudflare's `execution` option.

   - `"render"` runs the challenge when the widget is rendered. Right for
     a persistent challenge on a page where a token is always required
     (e.g. a form that's the whole reason the page exists).
   - `"execute"` renders the widget dormant and only runs the challenge
     when `execute()` is called. Right for short-lived forms like a
     sign-in dialog where we want a fresh, per-submission token so it
     can't go stale between render and click — and where password
     managers' synthetic input events have a chance to re-trigger bot
     heuristics in between.
*/
export type TurnstileExecution = "render" | "execute";

export interface TurnstileRenderOptions {
  appearance?: TurnstileAppearance;
  execution?: TurnstileExecution;
}

export interface TurnstileWidget {
  render(options?: TurnstileRenderOptions): void;
  /** Trigger the challenge (only meaningful when rendered with `execution: "execute"`). */
  execute(): void;
  getToken(): string;
  /**
   * Resolve with a fresh token, waiting up to `timeoutMs`. Intended to
   * be called right after `execute()` on submit — we wait for the
   * widget's `callback` to fire with a new token. If the challenge
   * escalates to visible interaction, the wait covers that too.
   */
  waitForToken(timeoutMs?: number): Promise<string>;
  reset(): void;
  remove(): void;
}

export function createTurnstile(selector: string): TurnstileWidget {
  let widgetId: string | null = null;
  // Last token produced by the widget's `callback`. We cache it ourselves
  // because we need to wake up any in-flight `waitForToken` promises, and
  // because `getResponse` returning "" doesn't tell us whether the challenge
  // is still running or has silently been invalidated.
  let currentToken = "";
  let pendingResolvers: Array<(token: string) => void> = [];

  const notifyToken = (token: string) => {
    currentToken = token;
    if (token) {
      const resolvers = pendingResolvers;
      pendingResolvers = [];
      resolvers.forEach((r) => r(token));
    }
  };

  // Wake up waiters with "" — lets callers stop waiting the full
  // timeout when Turnstile has told us it can't issue a token (hostname
  // blocked, network error, widget expired). Without this, a submit
  // stuck on "Verifying…" for 15s every time the site key is
  // misconfigured.
  const failPending = () => {
    const resolvers = pendingResolvers;
    pendingResolvers = [];
    resolvers.forEach((r) => r(""));
  };

  return {
    render({ appearance = "interaction-only", execution = "render" }: TurnstileRenderOptions = {}) {
      if (!window.turnstile || !SITE_KEY) return;
      const el = document.querySelector(selector) as HTMLElement | null;
      if (!el) return;
      if (widgetId !== null) {
        try {
          window.turnstile.remove(widgetId);
        } catch {}
        widgetId = null;
      }
      currentToken = "";
      widgetId = window.turnstile.render(selector, {
        sitekey: SITE_KEY,
        theme: "auto",
        appearance,
        execution,
        callback: (token: string) => notifyToken(token),
        "expired-callback": () => {
          currentToken = "";
        },
        "error-callback": (errorCode?: string) => {
          currentToken = "";
          // Log once so misconfigured site keys / blocked hostnames are
          // obvious in DevTools instead of silently timing out.
          // eslint-disable-next-line no-console
          console.warn("[turnstile] error-callback", errorCode);
          failPending();
        },
        "timeout-callback": () => {
          currentToken = "";
          // eslint-disable-next-line no-console
          console.warn("[turnstile] timeout-callback");
          failPending();
        },
      });
    },
    execute() {
      if (widgetId === null || !window.turnstile) return;
      // Each execute() starts a fresh challenge, so invalidate any
      // previously cached token so waitForToken actually waits for the
      // new one rather than resolving instantly with the stale value.
      currentToken = "";
      try {
        window.turnstile.execute(widgetId);
      } catch {}
    },
    getToken() {
      if (currentToken) return currentToken;
      if (widgetId === null || !window.turnstile) return "";
      return window.turnstile.getResponse(widgetId) || "";
    },
    waitForToken(timeoutMs = 10000) {
      if (currentToken) return Promise.resolve(currentToken);
      if (widgetId === null || !window.turnstile) return Promise.resolve("");
      return new Promise<string>((resolve) => {
        let settled = false;
        const done = (token: string) => {
          if (settled) return;
          settled = true;
          resolve(token);
        };
        pendingResolvers.push(done);
        setTimeout(() => done(""), timeoutMs);
      });
    },
    reset() {
      if (widgetId !== null && window.turnstile) {
        try {
          window.turnstile.reset(widgetId);
        } catch {}
      }
      currentToken = "";
    },
    remove() {
      if (widgetId !== null && window.turnstile) {
        try {
          window.turnstile.remove(widgetId);
        } catch {}
        widgetId = null;
      }
      currentToken = "";
      pendingResolvers = [];
    },
  };
}

// ---------- localStorage-based failure counter ----------
// After `threshold` failures within `windowMs`, shouldForceInteractive returns true.

const STORAGE_PREFIX = "turnstileFail:";

interface FailureRecord {
  count: number;
  firstAt: number;
}

function read(key: string): FailureRecord | null {
  try {
    const raw = localStorage.getItem(STORAGE_PREFIX + key);
    return raw ? (JSON.parse(raw) as FailureRecord) : null;
  } catch {
    return null;
  }
}

function write(key: string, rec: FailureRecord | null) {
  try {
    if (rec === null) localStorage.removeItem(STORAGE_PREFIX + key);
    else localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(rec));
  } catch {}
}

export function recordFailure(key: string, windowMs = 15 * 60 * 1000): void {
  if (!key) return;
  const now = Date.now();
  const existing = read(key);
  if (!existing || now - existing.firstAt > windowMs) {
    write(key, { count: 1, firstAt: now });
  } else {
    write(key, { count: existing.count + 1, firstAt: existing.firstAt });
  }
}

export function recordSuccess(key: string): void {
  if (!key) return;
  write(key, null);
}

export function shouldForceInteractive(key: string, threshold = 3, windowMs = 15 * 60 * 1000): boolean {
  if (!key) return false;
  const rec = read(key);
  if (!rec) return false;
  if (Date.now() - rec.firstAt > windowMs) {
    write(key, null);
    return false;
  }
  return rec.count >= threshold;
}
