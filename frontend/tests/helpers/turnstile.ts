import type { Page } from "@playwright/test";

export const turnstileScript = "https://challenges.cloudflare.com/turnstile/v0/api.js*";

export async function mockTurnstile(page: Page, outcome: "success" | "error" | "pending" = "success") {
  await page.route(turnstileScript, (route) =>
    route.fulfill({
      contentType: "application/javascript",
      body: `
        const widgets = new Map();
        let sequence = 0;
        window.turnstile = {
          render(selector, options) {
            const id = String(widgets.size);
            widgets.set(id, { selector, options });
            return id;
          },
          execute(id) {
            const { selector, options } = widgets.get(id);
            document.querySelector(selector).textContent = "Challenge in progress";
            if (${JSON.stringify(outcome)} === "success") options.callback("token-" + ++sequence);
            if (${JSON.stringify(outcome)} === "error") options["error-callback"]("200500");
          },
          getResponse() { return ""; },
          reset() {},
          remove(id) {
            document.querySelector(widgets.get(id).selector).textContent = "";
            widgets.delete(id);
          }
        };
      `,
    }),
  );
}
