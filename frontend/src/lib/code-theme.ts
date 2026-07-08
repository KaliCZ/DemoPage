import type { ThemeRegistrationRaw } from "shiki";

// Every color is a `--code-*` CSS variable that global.css aliases to a design
// token, so one theme serves both modes — the tokens themselves flip under `.dark`.
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
