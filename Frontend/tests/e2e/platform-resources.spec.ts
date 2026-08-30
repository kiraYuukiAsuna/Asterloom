import { expect, test, type Page } from "@playwright/test";

test("manages the complete platform hierarchy through the Web Console", async ({
  page,
}) => {
  test.setTimeout(90_000);
  page.setDefaultTimeout(10_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/tenants");
  await expect(page).toHaveURL("http://localhost:3000/tenants");

  const sessionResponse = await page.request.get("/api/auth/session");
  expect(sessionResponse.ok()).toBeTruthy();
  const session = (await sessionResponse.json()) as {
    actor: { subject: string };
  };

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = "tenant-e2e-" + suffix;
  const applicationSlug = "app-e2e-" + suffix;
  const environmentSlug = "prod-e2e-" + suffix;

  const tenantPanel = page.locator('[data-ui-action="list-tenants"]');
  await expect(tenantPanel).toBeVisible();
  await tenantPanel.getByLabel("Include archived").check();
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .first()
    .fill("E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();

  const tenantRow = page.getByTestId("tenant-" + tenantSlug);
  await expect(tenantRow).toContainText("E2E Tenant");
  await tenantRow.getByRole("button", { name: /edit tenant/i }).click();
  await tenantRow.getByLabel("Tenant display name").fill("E2E Tenant Updated");
  await tenantRow.locator('[data-ui-action="update-tenant"]').click();
  await expect(tenantRow).toContainText("E2E Tenant Updated");

  const applicationPanel = page.locator(
    '[data-ui-action="list-applications"]',
  );
  await expect(applicationPanel).toBeVisible();
  await applicationPanel.getByLabel("Include archived").check();
  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("E2E Application");
  await page.locator('[data-ui-action="create-application"]').click();

  const applicationRow = page.getByTestId("application-" + applicationSlug);
  await expect(applicationRow).toContainText("E2E Application");
  await applicationRow
    .getByRole("button", { name: /edit application/i })
    .click();
  await applicationRow
    .getByLabel("Application display name")
    .fill("E2E Application Updated");
  await applicationRow.locator('[data-ui-action="update-application"]').click();
  await expect(applicationRow).toContainText("E2E Application Updated");

  const environmentPanel = page.locator(
    '[data-ui-action="list-environments"]',
  );
  await expect(environmentPanel).toBeVisible();
  await environmentPanel.getByLabel("Include archived").check();
  const createEnvironmentForm = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await createEnvironmentForm.getByLabel("Slug").fill(environmentSlug);
  await createEnvironmentForm
    .getByLabel("Display name")
    .fill("E2E Production");
  await createEnvironmentForm.getByLabel("Type").selectOption("Production");
  await createEnvironmentForm.getByLabel("Protect from archival").check();
  await createEnvironmentForm
    .locator('[data-ui-action="create-environment"]')
    .click();

  const environmentRow = page.getByTestId("environment-" + environmentSlug);
  await expect(environmentRow).toContainText("Protected");
  await expect(
    environmentRow.locator('[data-ui-action="archive-environment"]'),
  ).toBeDisabled();
  await environmentRow
    .getByRole("button", { name: /edit environment/i })
    .click();
  await environmentRow
    .getByLabel("Display name")
    .fill("E2E Production Updated");
  await environmentRow.getByLabel("Protect from archival").uncheck();
  await environmentRow.locator('[data-ui-action="update-environment"]').click();
  await expect(environmentRow).toContainText("E2E Production Updated");

  await environmentRow.locator('[data-ui-action="archive-environment"]').click();
  await expect(
    environmentRow.locator('[data-ui-action="restore-environment"]'),
  ).toBeVisible();
  await environmentRow.locator('[data-ui-action="restore-environment"]').click();
  await expect(
    environmentRow.locator('[data-ui-action="archive-environment"]'),
  ).toBeEnabled();

  const membershipPanel = page.locator(
    '[data-ui-action="list-tenant-memberships"]',
  );
  await expect(membershipPanel).toBeVisible();
  await membershipPanel.getByLabel("Show removed memberships").check();
  await membershipPanel.getByLabel("Actor ID").fill(session.actor.subject);
  await membershipPanel
    .locator("form")
    .locator('[data-ui-action="set-tenant-membership"]')
    .click();

  const membershipRow = page.getByTestId(
    "membership-" + session.actor.subject,
  );
  await expect(membershipRow).toContainText("Active");
  await membershipRow
    .locator('[data-ui-action="remove-tenant-membership"]')
    .click();
  await expect(membershipRow).toContainText("Removed");
  await membershipRow
    .locator('[data-ui-action="set-tenant-membership"]')
    .click();
  await expect(membershipRow).toContainText("Active");

  await applicationRow
    .locator('[data-ui-action="archive-application"]')
    .click();
  await expect(
    applicationRow.locator('[data-ui-action="restore-application"]'),
  ).toBeVisible();
  await applicationRow
    .locator('[data-ui-action="restore-application"]')
    .click();
  await expect(
    applicationRow.locator('[data-ui-action="archive-application"]'),
  ).toBeVisible();

  await tenantRow.locator('[data-ui-action="archive-tenant"]').click();
  await expect(
    tenantRow.locator('[data-ui-action="restore-tenant"]'),
  ).toBeVisible();
  await tenantRow.locator('[data-ui-action="restore-tenant"]').click();
  await expect(tenantRow.locator('[data-ui-action="archive-tenant"]')).toBeVisible();
});

async function signIn(page: Page, returnTo: string) {
  await page.goto(returnTo);
  await expect(page).toHaveURL(/\/login\?returnTo=/);
  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL(/127\.0\.0\.1:5080\/passport\/login/);
  await page.locator('input[name="Email"]').fill("admin@asterloom.test");
  await page
    .locator('input[name="Password"]')
    .fill("Asterloom-E2E-Admin!2026");
  await page.getByRole("button", { name: "继续" }).click();
}
