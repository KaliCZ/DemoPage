import type { ThemeRegistrationRaw } from "shiki";

// One theme serves both modes because the `--code-*` variables it uses flip under `.dark` (see theme.css).
export const designTokenCodeTheme: ThemeRegistrationRaw = {
  name: "design-tokens",
  type: "light",
  colors: {
    "editor.foreground": "var(--code-foreground)",
    "editor.background": "var(--code-background)",
  },
  tokenColors: [
    {
      scope: ["comment", "string.quoted.docstring.multi"],
      settings: { foreground: "var(--code-comment)", fontStyle: "italic" },
    },
    {
      scope: ["string", "string.regexp", "markup.inline"],
      settings: { foreground: "var(--code-string)" },
    },
    {
      scope: [
        "keyword",
        "storage.modifier",
        "storage.type",
        "variable.language",
        "support.type.property-name.json",
        "punctuation.separator.key-value",
        "punctuation.definition.template-expression",
      ],
      settings: { foreground: "var(--code-keyword)" },
    },
    {
      scope: ["constant.numeric", "constant.language", "constant.character", "variable.other.constant", "support.constant"],
      settings: { foreground: "var(--code-constant)" },
    },
    {
      scope: [
        "entity.name.type",
        "entity.name.namespace",
        "entity.other.inherited-class",
        "entity.other.attribute-name",
        "support.class",
        "support.type",
      ],
      settings: { foreground: "var(--code-type)" },
    },
    {
      scope: ["entity.name.function", "support.function", "meta.function-call"],
      settings: { foreground: "var(--code-function)" },
    },
  ],
};
