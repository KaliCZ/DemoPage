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

  test("blog index lists posts and links to RSS", async ({ page }) => {
    await page.goto("/blog");
    await expect(page).toHaveTitle(/Blog/);
    await expect(page.getByRole("heading", { name: "Blog", level: 1 })).toBeVisible();
    await expect(page.getByRole("link", { name: /Shipping StrongTypes 1\.0/ })).toBeVisible();
    await expect(page.locator('link[rel="alternate"][type="application/rss+xml"]')).toHaveAttribute("href", "/rss.xml");
  });

  test("blog post renders chrome and signed-out reaction hint", async ({ page }) => {
    await page.goto("/blog/strongtypes-1-0");
    await expect(page).toHaveTitle(/StrongTypes 1\.0/);
    await expect(page.getByRole("heading", { name: "Reactions" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Comments" })).toBeVisible();
    await expect(page.getByText("Sign in to react")).toBeVisible();
    await expect(page.getByText("Sign in to post a comment.")).toBeVisible();
  });

  test("blog post links to upstream StrongTypes diagrams", async ({ page }) => {
    await page.goto("/blog/strongtypes-1-0");
    const diagramImg = page.locator('img[src*="github.com/KaliCZ/StrongTypes/raw/main/docs/diagrams/"]').first();
    await expect(diagramImg).toBeVisible();
  });

  test("rss feed includes the StrongTypes post", async ({ request }) => {
    const res = await request.get("/rss.xml");
    expect(res.status()).toBe(200);
    // Static hosts serve .xml as application/xml; the live edge config can
    // upgrade it to application/rss+xml. Either is valid for RSS readers.
    expect(res.headers()["content-type"]).toMatch(/application\/(rss\+)?xml/);
    const xml = await res.text();
    expect(xml).toContain("<rss");
    expect(xml).toContain("Shipping StrongTypes 1.0");
    expect(xml).toContain("https://www.kalandra.tech/blog/strongtypes-1-0");
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

  test("language switcher changes language", async ({ page }) => {
    await page.goto("/about");
    // Hover over language picker to open dropdown
    const langPicker = page
      .locator(".group:visible")
      .filter({ has: page.locator('[aria-label="Change language"]') })
      .first();
    await langPicker.hover();
    await page.getByRole("menuitem", { name: "Čeština" }).first().click();
    await expect(page).toHaveURL("/cs/about");
  });
});

test.describe("Dark mode", () => {
  test("toggle switches to dark mode", async ({ page }) => {
    await page.goto("/");
    const html = page.locator("html");
    await expect(html).not.toHaveClass(/dark/);

    await page.locator("#theme-toggle").click();
    await expect(html).toHaveClass(/dark/);

    await page.locator("#theme-toggle").click();
    await expect(html).not.toHaveClass(/dark/);
  });
});
