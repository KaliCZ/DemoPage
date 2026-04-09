/** Nested error map: field name → error code → localized message. */
export type ApiErrorMap = Record<string, Record<string, string>>;

/**
 * Extracts the first localized error from a ValidationProblemDetails response.
 * Returns the translated message if found, or null if the response doesn't
 * contain a recognized error code.
 *
 * Expected response shape (RFC 7807):
 * { "errors": { "fieldName": ["ErrorCode"] } }
 */
export async function getApiError(
  response: Response,
  errorMap: ApiErrorMap,
): Promise<string | null> {
  if (response.status < 400 || response.status >= 500) return null;
  try {
    const body = await response.json();
    if (body?.errors) {
      for (const field in body.errors) {
        const code = body.errors[field]?.[0];
        const msg = errorMap?.[field]?.[code];
        if (msg) return msg;
      }
    }
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
