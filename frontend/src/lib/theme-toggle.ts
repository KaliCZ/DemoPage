type ThemePref = "light" | "system" | "dark";

const STORAGE_KEY = "theme";
const prefersDark = window.matchMedia("(prefers-color-scheme: dark)");

// "system" is stored as no value at all, which the pre-paint script in Layout.astro relies on.
function getPref(): ThemePref {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === "light" || stored === "dark" ? stored : "system";
}

function resolveDark(pref: ThemePref): boolean {
  return pref === "dark" || (pref === "system" && prefersDark.matches);
}

// Must run synchronously inside the theme-flip callback so the view-transition snapshot captures the new src.
function swapThemedImages(willBeDark: boolean) {
  const imgs = document.querySelectorAll<HTMLImageElement>("img[data-src-dark]");
  imgs.forEach((img) => {
    const darkSrc = img.dataset.srcDark;
    if (!darkSrc) return;
    if (!img.dataset.srcLight) img.dataset.srcLight = img.getAttribute("src") ?? "";
    const lightSrc = img.dataset.srcLight;
    const next = willBeDark ? darkSrc : lightSrc;
    if (next && img.getAttribute("src") !== next) img.setAttribute("src", next);
  });
}

function syncPickerState(pref: ThemePref) {
  document.documentElement.dataset.themePref = pref;
  document.querySelectorAll<HTMLButtonElement>("[data-theme-option]").forEach((btn) => {
    btn.setAttribute("aria-pressed", btn.dataset.themeOption === pref ? "true" : "false");
  });
}

function applyTheme(pref: ThemePref) {
  const willBeDark = resolveDark(pref);
  swapThemedImages(willBeDark);
  document.documentElement.classList.toggle("dark", willBeDark);
  if (pref === "system") localStorage.removeItem(STORAGE_KEY);
  else localStorage.setItem(STORAGE_KEY, pref);
  syncPickerState(pref);
}

function setTheme(pref: ThemePref) {
  type DocWithVT = Document & { startViewTransition?: (cb: () => void) => unknown };
  const doc = document as DocWithVT;
  if (typeof doc.startViewTransition === "function") {
    doc.startViewTransition(() => applyTheme(pref));
    return;
  }
  document.documentElement.classList.add("no-transitions");
  applyTheme(pref);
  // Force reflow so the no-transitions class takes effect before we remove it
  document.documentElement.offsetHeight;
  document.documentElement.classList.remove("no-transitions");
}

document.querySelectorAll<HTMLButtonElement>("[data-theme-option]").forEach((btn) => {
  btn.addEventListener("click", () => setTheme(btn.dataset.themeOption as ThemePref));
});

prefersDark.addEventListener("change", () => {
  if (getPref() !== "system") return;
  const willBeDark = prefersDark.matches;
  swapThemedImages(willBeDark);
  document.documentElement.classList.toggle("dark", willBeDark);
});

syncPickerState(getPref());
