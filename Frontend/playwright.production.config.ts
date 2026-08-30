import { defineConfig, devices } from "@playwright/test";

function requireEnvironment(name: string) {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} is required for production Web E2E tests.`);
  }

  return value;
}

const webOrigin = new URL(
  requireEnvironment("ASTERLOOM_E2E_WEB_ORIGIN"),
).origin;

if (new URL(webOrigin).protocol !== "https:") {
  throw new Error("Production Web E2E tests require an HTTPS Web origin.");
}

requireEnvironment("ASTERLOOM_E2E_PASSPORT_ORIGIN");
requireEnvironment("ASTERLOOM_E2E_API_ORIGIN");
requireEnvironment("ASTERLOOM_E2E_ADMIN_EMAIL");
requireEnvironment("ASTERLOOM_E2E_ADMIN_PASSWORD");

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: true,
  retries: 1,
  workers: 1,
  outputDir: "test-results/production",
  reporter: [
    ["list"],
    [
      "html",
      {
        open: "never",
        outputFolder: "playwright-report-production",
      },
    ],
    ["json", { outputFile: "test-results/production-results.json" }],
  ],
  use: {
    baseURL: webOrigin,
    ignoreHTTPSErrors: false,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    {
      name: "production-chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
