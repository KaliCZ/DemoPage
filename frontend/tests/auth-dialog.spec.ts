import { test, expect, type Page } from "@playwright/test";

import { mockTurnstile, turnstileScript } from "./helpers/turnstile";
const recoveryEndpoint = /\/auth\/v1\/recover(?:\?|$)/;
const resetUnavailable = "We couldn’t confirm that the reset email was sent. Check your inbox, then try again later.";

async function openPasswordReset(page: Page, path = "/") {
  await page.goto(path);
  await page.locator("#auth-sign-in-desktop").click();
  await page.locator("#auth-dialog-forgot").click();
  await page.locator("#auth-email").fill("reset@example.com");
}

test("a recovery timeout restores the form and a retry sends a fresh CAPTCHA token", async ({ page }) => {
  await mockTurnstile(page);
  const requests: Array<{ email: string; gotrue_meta_security: { captcha_token: string } }> = [];
  let releaseResponse!: () => void;
  const responseReady = new Promise<void>((resolve) => (releaseResponse = resolve));
  await page.route(recoveryEndpoint, async (route) => {
    requests.push(route.request().postDataJSON());
    if (requests.length === 1) {
      await responseReady;
      await route.fulfill({ status: 504, contentType: "text/plain", body: "Gateway timeout" });
    } else {
      await route.fulfill({ json: {} });
    }
  });

  await openPasswordReset(page, "/about");
  const submit = page.locator("#auth-dialog-submit");
  await submit.click();
  await expect(submit).toHaveText("Sending reset link…");
  await expect(submit).toBeDisabled();
  await expect(page.locator("#auth-dialog-switch")).toBeDisabled();
  releaseResponse();

  await expect(page.getByRole("alert")).toHaveText(resetUnavailable);
  await expect(submit).toHaveText("Send Reset Link");
  await expect(submit).toBeEnabled();
  await expect(page.locator("#auth-turnstile-container")).toBeEmpty();
  expect(await page.evaluate(() => localStorage.getItem("turnstileFail:auth-fail"))).toBeNull();

  await submit.click();
  await expect(page.locator("#auth-dialog")).toBeHidden();
  await expect(page.getByText("a password reset link is on its way")).toBeVisible();
  expect(requests.map((request) => request.gotrue_meta_security.captcha_token)).toEqual(["token-1", "token-2"]);
  expect(requests.map((request) => request.email)).toEqual(["reset@example.com", "reset@example.com"]);
});

test("recovery service errors are localized", async ({ page }) => {
  await mockTurnstile(page);
  await page.route(recoveryEndpoint, (route) => route.fulfill({ status: 504, body: "Gateway timeout" }));
  await openPasswordReset(page, "/cs/about");
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toHaveText(
    "Nepodařilo se potvrdit odeslání e-mailu pro obnovení hesla. Zkontrolujte schránku a zkuste to později.",
  );
  await expect(page.locator("#auth-dialog-submit")).toHaveText("Poslat odkaz");
});

test("a failed CAPTCHA cannot submit a recovery request", async ({ page }) => {
  await mockTurnstile(page, "error");
  const pageErrors: string[] = [];
  const recoveryRequests: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  await page.route(recoveryEndpoint, (route) => {
    recoveryRequests.push(route.request().url());
    return route.fulfill({ json: {} });
  });
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toContainText("The security check could not finish.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(recoveryRequests).toEqual([]);
  expect(pageErrors).toEqual([]);
});

test("a blocked CAPTCHA script can be loaded again on retry", async ({ page }) => {
  await mockTurnstile(page);
  await page.route(turnstileScript, (route) => route.abort(), { times: 1 });
  await page.route(recoveryEndpoint, (route) => route.fulfill({ json: {} }));
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toContainText("The security check could not finish.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  await page.locator("#auth-dialog-submit").click();
  await expect(page.locator("#auth-dialog")).toBeHidden();
});

test("a CAPTCHA script that never initializes cannot leave the form busy", async ({ page }) => {
  await page.clock.install();
  await page.route(turnstileScript, (route) => route.fulfill({ contentType: "application/javascript", body: "" }));
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.locator('script[src*="challenges.cloudflare.com/turnstile"]')).toHaveCount(1);
  await page.clock.fastForward(10001);
  await expect(page.getByRole("alert")).toContainText("The security check could not finish.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
});

test("a CAPTCHA that never responds cannot submit without a token", async ({ page }) => {
  await page.clock.install();
  await mockTurnstile(page, "pending");
  const recoveryRequests: string[] = [];
  await page.route(recoveryEndpoint, (route) => {
    recoveryRequests.push(route.request().url());
    return route.fulfill({ json: {} });
  });
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.locator("#auth-turnstile-container")).toHaveText("Challenge in progress");
  await page.clock.fastForward(15001);
  await expect(page.getByRole("alert")).toContainText("The security check could not finish.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(recoveryRequests).toEqual([]);
});

test("a network failure restores the form without an unhandled exception", async ({ page }) => {
  await mockTurnstile(page);
  const pageErrors: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  await page.route(recoveryEndpoint, (route) => route.abort());
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toContainText("Could not reach the server.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(pageErrors).toEqual([]);
});

test("an unexpected auth exception restores the form without an unhandled rejection", async ({ page }) => {
  await mockTurnstile(page);
  const pageErrors: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  await openPasswordReset(page);
  await page.evaluate(async () => {
    const supabase = await (window as any).__supabaseReady;
    supabase.auth.resetPasswordForEmail = () => Promise.reject(new Error("Connection interrupted"));
  });
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toContainText("Could not reach the server.");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(pageErrors).toEqual([]);
});

test("invalid sign-in credentials still count as a failed auth attempt", async ({ page }) => {
  await mockTurnstile(page);
  await page.route(/\/auth\/v1\/token\?/, (route) =>
    route.fulfill({ status: 400, json: { code: "invalid_credentials", message: "Invalid login credentials" } }),
  );
  await page.goto("/");
  await page.locator("#auth-sign-in-desktop").click();
  await page.locator("#auth-email").fill("reset@example.com");
  await page.locator("#auth-password").fill("wrong-password");
  await page.locator("#auth-dialog-submit").click();
  await expect(page.getByRole("alert")).toHaveText("Invalid login credentials");
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem("turnstileFail:auth-fail")!).count)).toBe(1);
});

test("an invalid email never starts CAPTCHA verification", async ({ page }) => {
  const captchaRequests: string[] = [];
  await page.route(turnstileScript, (route) => {
    captchaRequests.push(route.request().url());
    return route.abort();
  });
  await openPasswordReset(page);
  await page.locator("#auth-email").fill("not-an-email");
  await page.locator("#auth-dialog-submit").click();
  await expect(page.locator("#auth-email")).toBeFocused();
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  expect(captchaRequests).toEqual([]);
});

test("a response from a closed dialog cannot close a newly opened dialog", async ({ page }) => {
  await mockTurnstile(page);
  let releaseResponse!: () => void;
  const responseReady = new Promise<void>((resolve) => (releaseResponse = resolve));
  await page.route(recoveryEndpoint, async (route) => {
    await responseReady;
    await route.fulfill({ json: {} });
  });
  await openPasswordReset(page);
  await page.locator("#auth-dialog-submit").click();
  await expect(page.locator("#auth-dialog-submit")).toHaveText("Sending reset link…");
  await page.locator("#auth-dialog-close").click();
  await page.locator("#auth-sign-in-desktop").click();
  await expect(page.locator("#auth-dialog-title")).toHaveText("Sign In");
  const completed = page.waitForResponse(recoveryEndpoint);
  releaseResponse();
  await completed;
  await expect(page.locator("#auth-dialog")).toBeVisible();
  await expect(page.locator("#auth-dialog-submit")).toBeEnabled();
  await expect(page.locator("#auth-dialog-submit")).toHaveText("Sign In");
});
