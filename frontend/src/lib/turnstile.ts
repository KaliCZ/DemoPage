// Reusable Cloudflare Turnstile helper.
// Usage:
//   const ts = createTurnstile('#my-container');
//   await ts.whenReady();
//   ts.render('interaction-only');
//   const token = ts.getToken();
//   ts.reset();
//
// Plus a localStorage-based escalation counter so forms can switch the widget
// to a visible checkbox ('always') after repeated failures per key (e.g. email).

declare global {
  interface Window {
    turnstile?: {
      render: (selector: string | HTMLElement, opts: Record<string, unknown>) => string;
      remove: (id: string) => void;
      reset: (id: string) => void;
      getResponse: (id: string) => string | undefined;
    };
  }
}

export type TurnstileAppearance = "interaction-only" | "always" | "execute";

const SITE_KEY = import.meta.env.PUBLIC_TURNSTILE_SITE_KEY as string | undefined;

export function turnstileEnabled(): boolean {
  return !!SITE_KEY;
}

export function whenTurnstileReady(): Promise<void> {
  return new Promise((resolve) => {
    if (window.turnstile) return resolve();
    const iv = setInterval(() => {
      if (window.turnstile) {
        clearInterval(iv);
        resolve();
      }
    }, 100);
  });
}

export interface TurnstileWidget {
  render(appearance?: TurnstileAppearance): void;
  getToken(): string;
  /**
   * Resolve with the current token, waiting up to `timeoutMs` if the
   * invisible challenge hasn't finished yet (or was invalidated by
   * programmatic form fill, e.g. a password manager). Resolves with "" on
   * timeout so callers can escalate to a visible challenge.
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

  return {
    render(appearance: TurnstileAppearance = "interaction-only") {
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
        callback: (token: string) => notifyToken(token),
        "expired-callback": () => {
          currentToken = "";
        },
        "error-callback": () => {
          currentToken = "";
        },
      });
    },
    getToken() {
      if (currentToken) return currentToken;
      if (widgetId === null || !window.turnstile) return "";
      return window.turnstile.getResponse(widgetId) || "";
    },
    waitForToken(timeoutMs = 3000) {
      const existing = this.getToken();
      if (existing) return Promise.resolve(existing);
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
