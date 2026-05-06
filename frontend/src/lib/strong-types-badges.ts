// Shield badge configuration for the /strong-types page. Not localized —
// the URLs, hrefs, and dimensions are the same for every locale, so they
// live here instead of being duplicated across i18n files. Only the alt
// text and the "downloads" badge label are translated.
//
// `width`/`height` are the typical pixel size each shields.io render
// produces. They're emitted as <img> attributes so the browser reserves
// layout space before the SVG arrives — without them, the badges popping
// in shifted the page (PageSpeed reported a 0.213 CLS contribution).

const repo = "KaliCZ/StrongTypes";
const pkg = "Kalicz.StrongTypes";

export type StrongTypesBadgeId = "nuget" | "downloads" | "build" | "license";

export interface StrongTypesBadge {
  id: StrongTypesBadgeId;
  href: string;
  width: number;
  height: number;
}

export const strongTypesBadges: readonly StrongTypesBadge[] = [
  { id: "nuget", href: `https://www.nuget.org/packages/${pkg}`, width: 96, height: 20 },
  { id: "downloads", href: `https://www.nuget.org/packages/${pkg}`, width: 124, height: 20 },
  { id: "build", href: `https://github.com/${repo}/actions/workflows/build.yml`, width: 108, height: 20 },
  { id: "license", href: `https://github.com/${repo}/blob/main/license.txt`, width: 96, height: 20 },
];

// Build the shields.io src URL for a badge. The "downloads" badge is the
// only one whose label is localized (e.g. "downloads" / "stahování"),
// passed in by the page from the active locale's i18n bundle.
export function strongTypesBadgeSrc(id: StrongTypesBadgeId, downloadsLabel: string): string {
  switch (id) {
    case "nuget":
      return `https://img.shields.io/nuget/v/${pkg}?label=nuget`;
    case "downloads":
      return `https://img.shields.io/nuget/dt/${pkg}?label=${encodeURIComponent(downloadsLabel)}`;
    case "build":
      return `https://img.shields.io/github/actions/workflow/status/${repo}/build.yml?branch=main&label=build`;
    case "license":
      return `https://img.shields.io/github/license/${repo}`;
  }
}
