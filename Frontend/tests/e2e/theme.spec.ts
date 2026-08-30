import { expect, test } from "@playwright/test";

import { signIn } from "./support/environment";

test("switches, persists, and follows the system color theme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto("/login");

  const root = page.locator("html");
  const toggle = page.getByTestId("theme-toggle");
  await expect(root).toHaveClass(/dark/);
  await expect(root).toHaveAttribute("data-theme-preference", "system");

  await toggle.click();
  await expect(root).toHaveClass(/light/);
  await expect(root).toHaveAttribute("data-theme-preference", "light");

  await page.reload();
  await expect(root).toHaveClass(/light/);
  await expect(root).toHaveAttribute("data-theme-preference", "light");

  await toggle.click();
  await expect(root).toHaveClass(/dark/);
  await expect(root).toHaveAttribute("data-theme-preference", "dark");

  await toggle.click();
  await expect(root).toHaveClass(/dark/);
  await expect(root).toHaveAttribute("data-theme-preference", "system");

  await page.emulateMedia({ colorScheme: "light" });
  await expect(root).toHaveClass(/light/);
});

test("uses light hero surfaces across every console capability", async ({ page }) => {
  test.setTimeout(120_000);
  page.setDefaultTimeout(25_000);
  await page.addInitScript(() => {
    window.localStorage.setItem("asterloom-theme", "light");
  });

  await signIn(page, "/releases");

  const surfaces = [
    { className: "theme-hero-violet", path: "/releases" },
    { className: "theme-hero-cyan", path: "/analytics/explorer" },
    { className: "theme-hero-violet", path: "/telemetry/health" },
    { className: "theme-hero-sky", path: "/operations/apis" },
    { className: "theme-hero-sky", path: "/storage/buckets" },
  ] as const;

  for (const surface of surfaces) {
    await page.goto(surface.path);
    await expect(page.locator("html")).toHaveClass(/light/);
    const hero = page.locator(`section.${surface.className}`).first();
    await expect(hero).toBeVisible();
    await expect(hero).toHaveCSS(
      "background-image",
      /rgba?\(255, 255, 255(?:, 0\.94)?\)/,
    );
  }
});
