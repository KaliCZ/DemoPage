function updateToggleIcons() {
  const isDark = document.documentElement.classList.contains("dark");
  const lightTip = document.documentElement.lang === "cs" ? "Přepnout na světlý režim" : "Switch to light mode";
  const darkTip = document.documentElement.lang === "cs" ? "Přepnout na tmavý režim" : "Switch to dark mode";
  const tip = isDark ? lightTip : darkTip;
  const btn = document.getElementById("theme-toggle");
  const btnM = document.getElementById("theme-toggle-mobile");
  if (btn) btn.title = tip;
  if (btnM) btnM.title = tip;
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

function applyThemeFlip() {
  const willBeDark = !document.documentElement.classList.contains("dark");
  swapThemedImages(willBeDark);
  document.documentElement.classList.toggle("dark", willBeDark);
  localStorage.setItem("theme", willBeDark ? "dark" : "light");
  updateToggleIcons();
}

// Entry point: prefer the View Transitions API for a smooth full-page
// crossfade; fall back to the original instant flip on older browsers.
// The legacy `no-transitions` snap is only kept as the fallback path —
// when view transitions run, we let the browser's snapshot/crossfade
// handle the whole page so individual element transitions are redundant.
function toggleTheme() {
  type DocWithVT = Document & { startViewTransition?: (cb: () => void) => unknown };
  const doc = document as DocWithVT;
  if (typeof doc.startViewTransition === "function") {
    doc.startViewTransition(applyThemeFlip);
    return;
  }
  document.documentElement.classList.add("no-transitions");
  applyThemeFlip();
  // Force reflow so the no-transitions class takes effect before we remove it
  document.documentElement.offsetHeight;
  document.documentElement.classList.remove("no-transitions");
}

document.getElementById("theme-toggle")?.addEventListener("click", toggleTheme);
document.getElementById("theme-toggle-mobile")?.addEventListener("click", toggleTheme);
updateToggleIcons();
