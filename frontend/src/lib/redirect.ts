// Parsed rather than prefix-checked: browsers resolve `/\evil.com` to https://evil.com.
export function sameOriginPath(target: string | null, fallback: string): string {
  if (!target) return fallback;
  try {
    const url = new URL(target, window.location.origin);
    return url.origin === window.location.origin ? url.pathname + url.search : fallback;
  } catch {
    return fallback;
  }
}
