import { getAccessToken } from "./auth";

export interface UserInfo {
  displayName?: string;
  avatarUrl?: string;
}

type CacheEntry = UserInfo & { _ts: number };

// profile.astro evicts entries by this literal key after an avatar change.
const CACHE_KEY = "userInfoCache";
const CACHE_TTL_MS = 5 * 60 * 1000;

function loadCache(): Record<string, CacheEntry> {
  try {
    const raw = JSON.parse(localStorage.getItem(CACHE_KEY) ?? "{}") as Record<string, CacheEntry>;
    const now = Date.now();
    const valid: Record<string, CacheEntry> = {};
    for (const id in raw) {
      if (raw[id]?._ts && now - raw[id]._ts < CACHE_TTL_MS) valid[id] = raw[id];
    }
    return valid;
  } catch {
    return {};
  }
}

function saveCache(cache: Record<string, CacheEntry>): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(cache));
  } catch {}
}

/** Best-effort: ids the API doesn't resolve, or a failed request, are simply absent from the result. */
export async function fetchUserInfo(apiUrl: string, userIds: (string | null)[]): Promise<Record<string, UserInfo>> {
  const ids = [...new Set(userIds.filter((id): id is string => !!id))];
  if (ids.length === 0) return {};

  const cache = loadCache();
  const uncached = ids.filter((id) => !(id in cache));
  if (uncached.length > 0) {
    try {
      const token = await getAccessToken();
      const res = await fetch(`${apiUrl}/api/users?ids=${uncached.join("&ids=")}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const data = (await res.json()) as Record<string, UserInfo>;
        const now = Date.now();
        for (const id of uncached) cache[id] = { ...data[id], _ts: now };
        saveCache(cache);
      }
    } catch {
      /* user info is decorative — callers fall back to the email/name on the record */
    }
  }

  const result: Record<string, UserInfo> = {};
  for (const id of ids) {
    if (cache[id]?.displayName) result[id] = cache[id];
  }
  return result;
}
