// Diagram URLs for the /strong-types page. Not localized — same SVG for every
// locale — so they live here instead of in the i18n JSON files. Each entry has
// a light and a dark variant; the page wires `srcDark` into the theme toggle
// via `data-src-dark` so the diagram flips with the rest of the page.
const branch = "claude/blissful-elion-43aab7";
const baseUrl = `https://raw.githubusercontent.com/KaliCZ/StrongTypes/${branch}/docs/diagrams`;

const diagram = (slug: string) => ({
  src: `${baseUrl}/${slug}.svg`,
  srcDark: `${baseUrl}/${slug}-dark.svg`,
});

export const strongTypesDiagrams = {
  impact: diagram("impact"),
  packageLayout: diagram("package-layout"),
  trycreateCreateFlow: diagram("trycreate-create-flow"),
  resultComposition: diagram("result-composition"),
  patchThreeState: diagram("patch-three-state"),
  maybeComposition: diagram("maybe-composition"),
} as const;

export type StrongTypesDiagramKey = keyof typeof strongTypesDiagrams;
