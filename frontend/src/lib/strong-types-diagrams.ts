// Deliberately `main`, not a release tag, so StrongTypes diagram updates show up without a site change.
const branch = "main";
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
