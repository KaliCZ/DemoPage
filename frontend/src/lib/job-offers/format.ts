import type { Locale } from "../../i18n/utils";

function localeOf(lang: Locale): string {
  return lang === "cs" ? "cs-CZ" : "en-US";
}

export function formatDate(iso: string, lang: Locale): string {
  return new Date(iso).toLocaleDateString(localeOf(lang));
}

export function formatDateTime(iso: string, lang: Locale): string {
  return new Date(iso).toLocaleString(localeOf(lang));
}

export function formatFileSize(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
}
