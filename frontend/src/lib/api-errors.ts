/** Nested error map: field name → error code → localized message. */
export type ApiErrorMap = Record<string, Record<string, string>>;

export type ApiErrorResult = {
  message: string | null;
  traceId?: string;
};

/** First localized field error and traceId from a ProblemDetails response; null when it has neither. */
export async function getApiError(response: Response, errorMap: ApiErrorMap): Promise<ApiErrorResult | null> {
  if (response.status < 400) return null;
  try {
    const body = await response.json();
    const traceId: string | undefined = body?.traceId ?? undefined;
    let message: string | null = null;

    if (body?.errors && response.status < 500) {
      for (const field in body.errors) {
        const code = body.errors[field]?.[0];
        const msg = errorMap?.[field]?.[code];
        if (msg) {
          message = msg;
          break;
        }
      }
    }

    if (message || traceId) return { message, traceId };
  } catch {}
  return null;
}

// Exposed for define:vars inline scripts, which can't import modules; guarded because islands import this during prerender.
declare global {
  interface Window {
    __getApiError: typeof getApiError;
  }
}
if (typeof window !== "undefined") window.__getApiError = getApiError;
