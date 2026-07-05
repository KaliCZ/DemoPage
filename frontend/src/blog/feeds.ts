import type { Locale } from "../i18n/utils";

/**
 * One feed per language edition of the site; a post appears in the feed(s) of
 * its declared languages. Kept free of other imports so Layout and Footer can
 * use it without pulling the post-page glob into every page.
 */
export const feeds: Record<Locale, { path: string; title: string }> = {
  en: { path: "/rss.xml", title: "kalandra.tech — Blog" },
  cs: { path: "/cs/rss.xml", title: "kalandra.tech — Blog (česky)" },
};
