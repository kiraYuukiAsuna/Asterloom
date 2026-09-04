import { expect, test, type Page } from "@playwright/test";

import {
  signIn,
  skipApplicationInitialization,
  webUrl,
} from "./support/environment";

test("manages SMTP accounts and mail delivery through every Mail API", async ({ page }) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(25_000);

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `mail-tenant-${suffix}`;
  const applicationSlug = `mail-app-${suffix}`;
  const accountName = `Mail E2E ${suffix}`;

  await signIn(page, "/tenants");
  await createScope(page, tenantSlug, applicationSlug);

  await page.getByLabel("Primary navigation").getByRole("link", { name: "Mail", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/mail/accounts"));
  await expect(page.locator("[data-mail-workspace]")).toHaveAttribute("data-hydrated", "true");
  await selectScope(page, tenantSlug, applicationSlug);

  await expect(page.locator('[data-ui-action="list-smtp-accounts"]')).toBeVisible();
  const createForm = page.locator("form").filter({ has: page.locator('[data-ui-action="create-smtp-account"]') });
  await createForm.getByLabel("Account name").fill(accountName);
  await createForm.getByLabel("SMTP host").fill("127.0.0.1");
  await createForm.getByLabel("Port", { exact: true }).fill("1");
  await createForm.getByLabel("Transport security", { exact: true }).selectOption("SMTP_SECURITY_START_TLS");
  await createForm.getByLabel("SMTP username").fill("mail-e2e@example.test");
  await createForm.getByLabel("Authorization code / password").fill("not-a-real-credential");
  await createForm.getByLabel("From address").fill("mail-e2e@example.test");
  await createForm.getByLabel("From name").fill("Mail E2E");
  await createForm.locator('[data-ui-action="create-smtp-account"]').click();

  const accountRow = page.locator('[data-ui-action="get-smtp-account"]').filter({ hasText: accountName });
  await expect(accountRow).toBeVisible();
  await accountRow.click();

  const updateForm = page.locator("form").filter({ has: page.locator('[data-ui-action="update-smtp-account"]') });
  await updateForm.getByLabel("From name").fill("Mail E2E Updated");
  await updateForm.locator('[data-ui-action="update-smtp-account"]').click();
  await expect(updateForm.getByLabel("From name")).toHaveValue("Mail E2E Updated");

  const testForm = page.locator("form").filter({ has: page.locator('[data-ui-action="test-smtp-account"]') });
  await testForm.getByLabel("Test recipient").fill("recipient@example.test");
  await testForm.locator('[data-ui-action="test-smtp-account"]').click();

  await page.getByLabel("Include archived").check();
  await page.locator('[data-ui-action="archive-smtp-account"]').click();
  await expect(page.locator('[data-ui-action="restore-smtp-account"]')).toBeVisible();
  await page.locator('[data-ui-action="restore-smtp-account"]').click();
  await expect(page.locator('[data-ui-action="archive-smtp-account"]')).toBeVisible();

  await page.getByRole("link", { name: "Compose & history", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/mail/deliveries"));
  await expect(page.locator('[data-ui-action="list-mail-deliveries"]')).toBeVisible();
  await expect(page.locator('[data-ui-action="get-mail-delivery"]').first()).toBeVisible();
  await page.locator('[data-ui-action="get-mail-delivery"]').first().click();

  const composeForm = page.locator("form").filter({ has: page.locator('[data-ui-action="send-email"]') });
  await composeForm.getByLabel("SMTP account").selectOption({ label: `${accountName} · mail-e2e@example.test` });
  await composeForm.getByLabel("To (comma, semicolon, or newline separated)").fill("recipient@example.test");
  await composeForm.getByLabel("Subject").fill(`Mail E2E ${suffix}`);
  await composeForm.getByLabel("Text body").fill("Asterloom Mail API browser journey");
  await composeForm.locator('[data-ui-action="send-email"]').click();
  await expect(page.locator('[data-ui-action="get-mail-delivery"]').first()).toBeVisible();
});

async function selectScope(page: Page, tenantSlug: string, applicationSlug: string) {
  await page.getByLabel("Mail tenant", { exact: true }).fill(`Mail E2E Tenant (${tenantSlug})`);
  await page.getByLabel("Mail application", { exact: true }).fill(`Mail E2E App (${applicationSlug})`);
}

async function createScope(page: Page, tenantSlug: string, applicationSlug: string) {
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page.getByLabel("Display name", { exact: true }).first().fill("Mail E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page.getByLabel("Display name", { exact: true }).nth(1).fill("Mail E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await skipApplicationInitialization(page);
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();
}
