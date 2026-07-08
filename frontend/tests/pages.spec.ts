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

test.describe("Theme picker", () => {
  test("light/system/dark segments re-style the page and persist the choice", async ({ page }) => {
    await page.goto("/");
    const html = page.locator("html");

    // Default (no stored preference) follows the OS, which Playwright runs as
    // light unless told otherwise.
    await expect(html).not.toHaveClass(/dark/);
    await expect(html).toHaveAttribute("data-theme-pref", "system");

    // Only the desktop picker is visible at the default viewport width.
    const opt = (value: string) => page.locator(`[data-theme-option="${value}"]:visible`);

    // Capture a few token-driven colors in light mode so we can confirm they
    // actually change — the .dark class flipping is a precondition, not proof.
    const readColors = () =>
      page.evaluate(() => ({
        body: getComputedStyle(document.body).backgroundColor,
        nav: getComputedStyle(document.querySelector("nav[aria-label]")!).backgroundColor,
        footer: getComputedStyle(document.querySelector("footer")!).backgroundColor,
      }));

    const lightColors = await readColors();

    await opt("dark").click();
    await expect(html).toHaveClass(/dark/);
    await expect(html).toHaveAttribute("data-theme-pref", "dark");
    await expect(opt("dark")).toHaveAttribute("aria-pressed", "true");

    const darkColors = await readColors();
    expect(darkColors.body).not.toBe(lightColors.body);
    expect(darkColors.nav).not.toBe(lightColors.nav);
    expect(darkColors.footer).not.toBe(lightColors.footer);

    await opt("light").click();
    await expect(html).not.toHaveClass(/dark/);
    await expect(html).toHaveAttribute("data-theme-pref", "light");

    // Switching back to light restores the original colors.
    const restoredColors = await readColors();
    expect(restoredColors).toEqual(lightColors);

    // The chosen preference survives a reload.
    await page.reload();
    await expect(page.locator("html")).toHaveAttribute("data-theme-pref", "light");
    await expect(page.locator("html")).not.toHaveClass(/dark/);

    // The system segment clears the override (no stored value).
    await opt("system").click();
    await expect(page.locator("html")).toHaveAttribute("data-theme-pref", "system");
    expect(await page.evaluate(() => localStorage.getItem("theme"))).toBeNull();
  });
});

test.describe("Blog", () => {
  test("blog index lists the first post", async ({ page }) => {
    await page.goto("/blog");
    await expect(page).toHaveTitle(/Blog/);
    await expect(page.getByRole("link", { name: /Zero-Code Validations/ })).toBeVisible();
  });

  test("blog index renders in Czech with the Czech variant", async ({ page }) => {
    await page.goto("/cs/blog");
    await expect(page.locator("html")).toHaveAttribute("lang", "cs");
    const postLink = page.getByRole("link", { name: /Validace v \.NET API/ });
    await expect(postLink).toBeVisible();
    await expect(postLink).toHaveAttribute("href", "/cs/blog/zero-code-validations-in-your-dotnet-api");
  });

  test("blog post renders the article with highlighted code", async ({ page }) => {
    await page.goto("/blog/zero-code-validations-in-your-dotnet-api");
    await expect(page).toHaveTitle(/Zero-Code Validations/);
    await expect(page.locator("article h1")).toContainText("Zero-Code Validations in Your .NET API");
    await expect(page.locator("pre.astro-code").first()).toBeVisible();
  });

  test("czech post variant renders its own body and hreflang pair", async ({ page }) => {
    await page.goto("/cs/blog/zero-code-validations-in-your-dotnet-api");
    await expect(page.locator("html")).toHaveAttribute("lang", "cs");
    await expect(page.locator("article h1")).toContainText("Validace v .NET API bez jediného řádku kódu");
    await expect(page.locator("pre.astro-code").first()).toBeVisible();
    // The bilingual post claims both languages in its head.
    await expect(page.locator('link[rel="alternate"][hreflang="en"]')).toHaveAttribute(
      "href",
      "https://www.kalandra.tech/blog/zero-code-validations-in-your-dotnet-api",
    );
  });

  test("language picker on a bilingual post targets the translated post", async ({ page }) => {
    await page.goto("/blog/zero-code-validations-in-your-dotnet-api");
    await expect(page.getByRole("link", { name: "Switch to Czech" }).first()).toHaveAttribute(
      "href",
      "/cs/blog/zero-code-validations-in-your-dotnet-api",
    );
  });

  test("nav includes Blog and navigates to the index", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "Blog" }).first().click();
    await expect(page).toHaveURL("/blog");
  });

  test("blog index and footer expose the single RSS feed in both locales", async ({ page }) => {
    await page.goto("/blog");
    await expect(page.getByRole("link", { name: "Subscribe via RSS" })).toHaveAttribute("href", "/rss.xml");
    await expect(page.getByRole("link", { name: "Subscribe to the blog RSS feed" })).toHaveAttribute("href", "/rss.xml");

    await page.goto("/cs/blog");
    await expect(page.getByRole("link", { name: "Odebírat přes RSS" })).toHaveAttribute("href", "/rss.xml");
    await expect(page.getByRole("link", { name: "Odebírat blog přes RSS" })).toHaveAttribute("href", "/rss.xml");
  });

  test("every page advertises the one feed for autodiscovery", async ({ page }) => {
    await page.goto("/about");
    const feedLinks = page.locator('link[rel="alternate"][type="application/rss+xml"]');
    await expect(feedLinks).toHaveCount(1);
    await expect(feedLinks).toHaveAttribute("href", "/rss.xml");
  });

  test("rss.xml lists each post once, both titles for a bilingual post", async ({ request }) => {
    const response = await request.get("/rss.xml");
    expect(response.ok()).toBeTruthy();
    expect(response.headers()["content-type"]).toContain("xml");
    const body = await response.text();
    expect(body).toContain("<rss");
    // Bilingual post: one item, language tags + both titles (English first), linked to the English page.
    expect(body).toContain("[EN]/[CS] Zero-Code Validations in Your .NET API / Validace v .NET API bez jediného řádku kódu");
    expect(body).toContain("<link>https://www.kalandra.tech/blog/zero-code-validations-in-your-dotnet-api</link>");
    // The Czech URL is not a separate entry — one item per post, not per language.
    expect(body).not.toContain("<link>https://www.kalandra.tech/cs/blog/");
    // Validator-completeness fields: self link, feed language, last change.
    expect(body).toContain('<atom:link href="https://www.kalandra.tech/rss.xml" rel="self" type="application/rss+xml"/>');
    expect(body).toContain("<language>en</language>");
    expect(body).toContain("<lastBuildDate>");
  });

  test("sitemap.xml covers static pages, the blog index, and blog posts, skips private pages", async ({ request }) => {
    const response = await request.get("/sitemap.xml");
    expect(response.ok()).toBeTruthy();
    const body = await response.text();
    expect(body).toContain("<loc>https://www.kalandra.tech/about</loc>");
    // Exact match — the /blog prefix on post URLs must not stand in for the index itself.
    expect(body).toContain("<loc>https://www.kalandra.tech/blog</loc>");
    expect(body).toContain("<loc>https://www.kalandra.tech/cs/blog</loc>");
    expect(body).toContain("<loc>https://www.kalandra.tech/blog/zero-code-validations-in-your-dotnet-api</loc>");
    // Bilingual post: the Czech URL is a first-class sitemap entry, not just an alternate.
    expect(body).toContain("<loc>https://www.kalandra.tech/cs/blog/zero-code-validations-in-your-dotnet-api</loc>");
    // The post's updatedDate — it wins over pubDate for <lastmod>.
    expect(body).toContain("<lastmod>2026-07-08</lastmod>");
    expect(body).toContain('hreflang="cs"');
    expect(body).not.toContain("/profile");
    expect(body).not.toContain("/admin");
    expect(body).not.toContain("/auth/callback");
  });
});
