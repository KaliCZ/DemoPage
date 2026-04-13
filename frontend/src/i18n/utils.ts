export type Locale = "en" | "cs";
export const locales: Locale[] = ["en", "cs"];
export const defaultLocale: Locale = "en";

/**
 * Returns the locale-prefixed path for a given page.
 * English (default locale) has no prefix; Czech uses /cs.
 */
export function localePath(lang: Locale, page: string): string {
  const base = lang === "cs" ? "/cs" : "";
  return page === "home" ? `${base}/` : `${base}/${page}`;
}

/**
 * Returns the alternate-language URL for the language picker.
 */
export function alternateLangUrl(targetLang: Locale, activePage: string): string {
  const base = targetLang === "cs" ? "/cs" : "";
  return activePage === "home" ? `${base}/` : `${base}/${activePage}`;
}
