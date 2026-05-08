import { test, expect } from "@playwright/test";

test.describe("Page rendering", () => {
  test("home page loads in English", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle(/kalandra\.tech/);
    await expect(page.locator("nav")).toBeVisible();
    await expect(page.getByRole("link", { name: "Hire Me" })).toBeVisible();
  });

  test("home page loads in Czech", async ({ page }) => {
    await page.goto("/cs/");
    await expect(page).toHaveTitle(/kalandra\.tech/);
    await expect(page.getByRole("link", { name: "Najměte mě" })).toBeVisible();
  });

  test("hire-me page shows login prompt when not authenticated", async ({ page }) => {
    await page.goto("/hire-me");
    await expect(page.locator("#login-prompt")).toBeVisible();
    await expect(page.locator("#job-offer-form-section")).toBeHidden();
  });

  test("job-offers page shows login prompt when not authenticated", async ({ page }) => {
    await page.goto("/job-offers");
    await expect(page.locator("#login-prompt")).toBeVisible();
    await expect(page.locator("#offers-list-section")).toBeHidden();
  });

  test("admin job-offers page shows access denied when not authenticated", async ({ page }) => {
    await page.goto("/admin/job-offers");
    // Wait for auth check to complete (loading spinner disappears)
    await expect(page.locator("#admin-loading")).toBeHidden({ timeout: 10000 });
    await expect(page.locator("#access-denied")).toBeVisible();
    await expect(page.locator("#offers-list-section")).toBeHidden();
  });

  test("about page loads", async ({ page }) => {
    await page.goto("/about");
    await expect(page).toHaveTitle(/About/);
  });

  test("project page loads", async ({ page }) => {
    await page.goto("/project");
    await expect(page).toHaveTitle(/Project/);
  });

  test("projects index page loads", async ({ page }) => {
    await page.goto("/projects");
    await expect(page).toHaveTitle(/Projects/);
  });

  test("strong-types page loads", async ({ page }) => {
    await page.goto("/strong-types");
    await expect(page).toHaveTitle(/StrongTypes/);
  });
});

test.describe("Navigation", () => {
  test("can navigate between pages via nav links", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "About Me" }).first().click();
    await expect(page).toHaveURL("/about");

    await page.getByRole("link", { name: "Hire Me" }).first().click();
    await expect(page).toHaveURL("/hire-me");
  });

  test("language switcher changes language and renders translated content", async ({ page }) => {
    await page.goto("/about");
    const nav = page.locator("nav[aria-label]");

    // English content present before the switch.
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
    await expect(nav.getByRole("link", { name: "About Me" }).first()).toBeVisible();
    await expect(nav.getByRole("link", { name: "Hire Me" }).first()).toBeVisible();

    await page.getByRole("link", { name: "Switch to Czech" }).first().click();
    await expect(page).toHaveURL("/cs/about");

    // Czech content actually rendered — not just URL change.
    await expect(page.locator("html")).toHaveAttribute("lang", "cs");
    await expect(nav.getByRole("link", { name: "O mně" }).first()).toBeVisible();
    await expect(nav.getByRole("link", { name: "Najměte mě" }).first()).toBeVisible();

    // And the reverse direction also flips content back to English.
    await page.getByRole("link", { name: "Přepnout do angličtiny" }).first().click();
    await expect(page).toHaveURL("/about");
    await expect(page.locator("html")).toHaveAttribute("lang", "en");
    await expect(nav.getByRole("link", { name: "About Me" }).first()).toBeVisible();
  });
});

test.describe("Dark mode", () => {
  test("toggle switches to dark mode and re-styles the page", async ({ page }) => {
    await page.goto("/");
    const html = page.locator("html");
    await expect(html).not.toHaveClass(/dark/);

    // Capture a few token-driven colors in light mode so we can confirm they
    // actually change — the .dark class flipping is a precondition, not proof.
    const readColors = () =>
      page.evaluate(() => ({
        body: getComputedStyle(document.body).backgroundColor,
        nav: getComputedStyle(document.querySelector("nav[aria-label]")!).backgroundColor,
        footer: getComputedStyle(document.querySelector("footer")!).backgroundColor,
      }));

    const lightColors = await readColors();

    await page.locator("#theme-toggle").click();
    await expect(html).toHaveClass(/dark/);

    const darkColors = await readColors();
    expect(darkColors.body).not.toBe(lightColors.body);
    expect(darkColors.nav).not.toBe(lightColors.nav);
    expect(darkColors.footer).not.toBe(lightColors.footer);

    await page.locator("#theme-toggle").click();
    await expect(html).not.toHaveClass(/dark/);

    // Toggling back restores the original light-mode colors.
    const restoredColors = await readColors();
    expect(restoredColors).toEqual(lightColors);
  });
});
