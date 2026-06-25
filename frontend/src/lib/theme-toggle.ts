type ThemePref = "light" | "system" | "dark";

const STORAGE_KEY = "theme";
const prefersDark = window.matchMedia("(prefers-color-scheme: dark)");

// "system" is represented by the absence of a stored value, matching the
// pre-paint script in Layout.astro — an unset preference follows the OS.
function getPref(): ThemePref {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === "light" || stored === "dark" ? stored : "system";
}

function resolveDark(pref: ThemePref): boolean {
  return pref === "dark" || (pref === "system" && prefersDark.matches);
}

// Swap any image marked with data-src-dark to its theme-appropriate src.
// Runs synchronously inside the theme-flip callback so the new state is
// captured by the view-transition snapshot.
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

// Reflect the active preference on the picker: the data-theme-pref attribute
// drives the CSS highlight, and aria-pressed announces the choice.
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

// Entry point: prefer the View Transitions API for a smooth full-page
// crossfade; fall back to the instant flip on older browsers.
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

// While following the system, track live OS changes (e.g. the nightly
// light→dark switch) without a stored override taking precedence.
prefersDark.addEventListener("change", () => {
  if (getPref() !== "system") return;
  const willBeDark = prefersDark.matches;
  swapThemedImages(willBeDark);
  document.documentElement.classList.toggle("dark", willBeDark);
});

syncPickerState(getPref());
