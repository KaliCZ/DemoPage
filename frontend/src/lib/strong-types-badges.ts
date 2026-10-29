const repo = "KaliCZ/StrongTypes";
const pkg = "Kalicz.StrongTypes";

export type StrongTypesBadgeId = "nuget" | "downloads" | "build" | "license";

export interface StrongTypesBadge {
  id: StrongTypesBadgeId;
  href: string;
  width: number;
  height: number;
}

// width/height are shields.io's typical render size, so each <img> reserves its space and the badges don't shift the page.
export const strongTypesBadges: readonly StrongTypesBadge[] = [
  { id: "nuget", href: `https://www.nuget.org/packages/${pkg}`, width: 96, height: 20 },
  { id: "downloads", href: `https://www.nuget.org/packages/${pkg}`, width: 112, height: 20 },
  { id: "build", href: `https://github.com/${repo}/actions/workflows/build.yml`, width: 108, height: 20 },
  { id: "license", href: `https://github.com/${repo}/blob/main/license.txt`, width: 96, height: 20 },
];

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
