import { getApiError, type ApiErrorMap } from "./api-errors";

/** Localized fallback messages for failed API requests, from common.json. */
export interface RequestErrorStrings {
  generic: string;
  notFound: string;
  serverError: string;
  networkError: string;
}

/** Maps an HTTP status (undefined = network failure) to its localized message. */
export function requestErrorMessage(strings: RequestErrorStrings, status?: number): string {
  if (!status) return strings.networkError;
  if (status === 404) return strings.notFound;
  if (status >= 500) return strings.serverError;
  return strings.generic;
}

/** Returns the message it showed in the snackbar so callers can also render it inline. */
export async function showRequestError(
  response: Response | undefined,
  errorMap: ApiErrorMap,
  strings: RequestErrorStrings,
): Promise<string> {
  const result = response ? await getApiError(response, errorMap) : null;
  const message = result?.message ?? requestErrorMessage(strings, response?.status);
  (window as any).__showSnackbar?.(message, "error", 8000, result?.traceId);
  return message;
}
