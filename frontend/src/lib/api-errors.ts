/** Nested error map: field name → error code → localized message. */
export type ApiErrorMap = Record<string, Record<string, string>>;

/** Result from parsing a ProblemDetails API error response. */
export type ApiErrorResult = {
  message: string | null;
  traceId?: string;
};

/**
 * Extracts the first localized error and traceId from a ProblemDetails response.
 * Returns an object with the translated message (if found) and traceId (if present),
 * or null if the response doesn't contain either.
 *
 * Expected response shape (RFC 7807):
 * { "errors": { "fieldName": ["ErrorCode"] }, "traceId": "00-..." }
 */
export async function getApiError(
  response: Response,
  errorMap: ApiErrorMap,
): Promise<ApiErrorResult | null> {
  if (response.status < 400) return null;
  try {
    const body = await response.json();
    const traceId: string | undefined = body?.traceId ?? undefined;
    let message: string | null = null;

    if (body?.errors && response.status < 500) {
      for (const field in body.errors) {
        const code = body.errors[field]?.[0];
        const msg = errorMap?.[field]?.[code];
        if (msg) { message = msg; break; }
      }
    }

    if (message || traceId) return { message, traceId };
  } catch {}
  return null;
}

// Expose globally for inline define:vars scripts that can't use ES imports
declare global {
  interface Window {
    __getApiError: typeof getApiError;
  }
}
window.__getApiError = getApiError;
