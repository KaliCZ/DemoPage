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

function loadTurnstileScript(): HTMLScriptElement {
  const existing = document.querySelector<HTMLScriptElement>(`script[src^="${TURNSTILE_SCRIPT_SRC}"]`);
  if (existing) return existing;
  const script = document.createElement("script");
  script.src = `${TURNSTILE_SCRIPT_SRC}?render=explicit`;
  script.async = true;
  script.defer = true;
  document.head.appendChild(script);
  return script;
}

export function whenTurnstileReady(): Promise<boolean> {
  if (window.turnstile) return Promise.resolve(true);
  if (!SITE_KEY) return Promise.resolve(false);
  return new Promise((resolve) => {
    const script = loadTurnstileScript();
    const finish = (ready: boolean) => {
      clearInterval(interval);
      clearTimeout(timeout);
      script.removeEventListener("error", onError);
      if (!ready) script.remove();
      resolve(ready);
    };
    const onError = () => finish(false);
    const interval = setInterval(() => {
      if (window.turnstile) finish(true);
    }, 100);
    const timeout = setTimeout(() => finish(false), 10000);
    script.addEventListener("error", onError, { once: true });
  });
}

/** `"execute"` defers the challenge to `execute()` so a short-lived form gets a fresh token at submit, not one that went stale since render. */
export type TurnstileExecution = "render" | "execute";

export interface TurnstileRenderOptions {
  appearance?: TurnstileAppearance;
  execution?: TurnstileExecution;
}

export interface TurnstileWidget {
  render(options?: TurnstileRenderOptions): void;
  /** Only meaningful when rendered with `execution: "execute"`. */
  execute(): void;
  getToken(): string;
  /** Resolves with "" on timeout or when the challenge fails. */
  waitForToken(timeoutMs?: number): Promise<string>;
  reset(): void;
  remove(): void;
}

export function createTurnstile(selector: string): TurnstileWidget {
  let widgetId: string | null = null;
  // Cached ourselves because getResponse's "" can't distinguish a running challenge from an invalidated one.
  let currentToken = "";
  let challengeFailed = false;
  let pendingResolvers: Array<(token: string) => void> = [];

  const notifyToken = (token: string) => {
    currentToken = token;
    if (token) {
      const resolvers = pendingResolvers;
      pendingResolvers = [];
      resolvers.forEach((r) => r(token));
    }
  };

  const failPending = () => {
    challengeFailed = true;
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
      challengeFailed = false;
      widgetId = window.turnstile.render(selector, {
        sitekey: SITE_KEY,
        theme: "auto",
        appearance,
        execution,
        callback: (token: string) => notifyToken(token),
        "expired-callback": () => {
          currentToken = "";
          failPending();
        },
        "error-callback": (errorCode?: string) => {
          currentToken = "";
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
      currentToken = "";
      challengeFailed = false;
      try {
        window.turnstile.execute(widgetId);
      } catch {
        failPending();
      }
    },
    getToken() {
      if (currentToken) return currentToken;
      if (widgetId === null || !window.turnstile) return "";
      return window.turnstile.getResponse(widgetId) || "";
    },
    waitForToken(timeoutMs = 10000) {
      if (currentToken) return Promise.resolve(currentToken);
      if (challengeFailed) return Promise.resolve("");
      if (widgetId === null || !window.turnstile) return Promise.resolve("");
      return new Promise<string>((resolve) => {
        let settled = false;
        const done = (token: string) => {
          if (settled) return;
          settled = true;
          clearTimeout(timeout);
          pendingResolvers = pendingResolvers.filter((pending) => pending !== done);
          resolve(token);
        };
        pendingResolvers.push(done);
        const timeout = setTimeout(() => done(""), timeoutMs);
      });
    },
    reset() {
      failPending();
      if (widgetId !== null && window.turnstile) {
        try {
          window.turnstile.reset(widgetId);
        } catch {}
      }
      currentToken = "";
      challengeFailed = false;
    },
    remove() {
      failPending();
      if (widgetId !== null && window.turnstile) {
        try {
          window.turnstile.remove(widgetId);
        } catch {}
        widgetId = null;
      }
      currentToken = "";
      challengeFailed = false;
    },
  };
}

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
