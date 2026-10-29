import { defineConfig, devices } from "@playwright/test";

/** Static-site tests only; tests/e2e needs the backend and runs via playwright.e2e.config.ts. */
export default defineConfig({
  testDir: "./tests",
  testIgnore: "**/e2e/**",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? "github" : "html",
  use: {
    baseURL: "http://localhost:4321",
    trace: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run build && npm run serve",
    port: 4321,
    reuseExistingServer: !process.env.CI,
    timeout: 60000,
  },
});
