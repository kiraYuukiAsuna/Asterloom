import { expect, type Page } from "@playwright/test";

function normalizeOrigin(value: string, variableName: string) {
  let url: URL;

  try {
    url = new URL(value);
  } catch {
    throw new Error(`${variableName} must be an absolute HTTP(S) origin.`);
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new Error(`${variableName} must use HTTP or HTTPS.`);
  }

  return url.origin;
}

export const webOrigin = normalizeOrigin(
  process.env.ASTERLOOM_E2E_WEB_ORIGIN ?? "http://localhost:3000",
  "ASTERLOOM_E2E_WEB_ORIGIN",
);

export const passportOrigin = normalizeOrigin(
  process.env.ASTERLOOM_E2E_PASSPORT_ORIGIN ?? "http://127.0.0.1:5080",
  "ASTERLOOM_E2E_PASSPORT_ORIGIN",
);

export const apiOrigin = normalizeOrigin(
  process.env.ASTERLOOM_E2E_API_ORIGIN ?? "http://127.0.0.1:5080",
  "ASTERLOOM_E2E_API_ORIGIN",
);

export const adminEmail =
  process.env.ASTERLOOM_E2E_ADMIN_EMAIL ?? "admin@asterloom.test";

const adminPassword =
  process.env.ASTERLOOM_E2E_ADMIN_PASSWORD ?? "Asterloom-E2E-Admin!2026";

export function webUrl(path: string) {
  return new URL(path, `${webOrigin}/`).toString();
}

export function apiUrl(path: string) {
  return new URL(path, `${apiOrigin}/`).toString();
}

export async function signIn(
  page: Page,
  returnTo: string,
  email = adminEmail,
  password = adminPassword,
) {
  await page.goto(returnTo);
  await expect(page).toHaveURL(
    (url) => url.origin === webOrigin && url.pathname === "/login",
  );
  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL(
    (url) =>
      url.origin === passportOrigin && url.pathname === "/passport/login",
  );
  await page.locator('input[name="Email"]').fill(email);
  await page.locator('input[name="Password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  await expect(page).toHaveURL(webUrl(returnTo));
}

export async function skipApplicationInitialization(page: Page) {
  const dialog = page.getByTestId("application-initialization-dialog");
  await expect(dialog).toBeVisible();
  await dialog
    .locator('[data-ui-action="application-initialization-skip"]')
    .click();
  await expect(dialog).toHaveCount(0);
}
