import { expect, test, type Page } from "@playwright/test";

import { signIn, webUrl } from "./support/environment";

test("manages dynamic configuration through every admin and runtime API", async ({
  page,
}) => {
  test.setTimeout(150_000);
  page.setDefaultTimeout(15_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/tenants");
  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `config-tenant-${suffix}`;
  const applicationSlug = `config-app-${suffix}`;
  const environmentSlug = `config-env-${suffix}`;
  const segmentKey = `config-users-${suffix}`;
  const entryKey = `ui.banner-${suffix}`;

  await createScope(page, tenantSlug, applicationSlug, environmentSlug);
  await createSegment(page, tenantSlug, applicationSlug, environmentSlug, segmentKey);

  await page.getByRole("link", { name: "Configuration" }).click();
  await expect(page).toHaveURL(webUrl("/config"));
  await expect(page.locator("[data-config-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  await page
    .getByLabel("Configuration tenant")
    .selectOption({ label: `Config E2E Tenant (${tenantSlug})` });
  await page
    .getByLabel("Configuration application")
    .selectOption({ label: `Config E2E App (${applicationSlug})` });
  await page
    .getByLabel("Configuration environment")
    .selectOption({ label: `Config E2E Environment (${environmentSlug})` });

  const list = page.locator('[data-ui-action="list-config-entries"]');
  await expect(list).toBeVisible();
  const create = page.locator('[data-ui-action="create-config-entry"]');
  await create.locator('input[name="createConfigKey"]').fill(entryKey);
  await create.locator('input[name="createConfigDisplayName"]').fill("UI banner");
  await create
    .locator('textarea[name="createConfigDescription"]')
    .fill("Complete dynamic configuration browser flow");
  await create.locator('input[name="createDefaultValue"]').fill("stable");
  await create.getByRole("button", { name: "Add segment override" }).click();
  await create
    .locator('select[name="createTargetingSegment"]')
    .selectOption({ label: `Config users (${segmentKey})` });
  await create.locator('input[name="createTargetingValue"]').fill("preview");
  await create.getByRole("button", { name: "Create configuration" }).click();

  const row = page.getByTestId(`config-entry-${entryKey}`);
  await expect(row).toContainText("UI banner");
  await Promise.all([
    page.waitForResponse(
      (response) =>
        response.request().method() === "GET" &&
        /\/config\/entries\/[0-9a-f-]+(?:\?|$)/i.test(response.url()),
    ),
    row.locator('[data-ui-action="get-config-entry"]').click(),
  ]);

  const draft = page.locator('[data-ui-action="update-config-draft"]');
  await draft.locator('input[name="editConfigDisplayName"]').fill("UI banner updated");
  await draft.getByRole("button", { name: "Save draft" }).click();
  await expect(row).toContainText("UI banner updated");
  await page.locator('[data-ui-action="validate-config-draft"]').click();
  await expect(page.getByText("Draft is publishable")).toBeVisible();
  await page.locator('[data-ui-action="diff-config-draft"]').click();
  await expect(page.getByText(/changed path/)).toBeVisible();
  await page.locator('[data-ui-action="publish-config-entry"]').click();
  await expect(row).toContainText("published 1");

  const revisions = page.locator('[data-ui-action="list-config-revisions"]');
  await expect(revisions).toContainText("Revision 1");
  const lab = page.getByRole("heading", { name: "Effective value lab" }).locator("../..");
  await lab.locator('input[name="configTargetingKey"]').fill(`user-${suffix}`);
  await lab.locator('input[name="configRegion"]').fill("cn");
  await page.locator('[data-ui-action="preview-config-value"]').click();
  await expect(lab).toContainText("preview");
  await page.locator('[data-ui-action="get-config-snapshot"]').click();
  await expect(lab).toContainText(entryKey);
  await lab.getByRole("button", { name: "Conditional refresh" }).click();
  await page.locator('[data-ui-action="get-server-config-snapshot"]').click();
  await expect(lab).toContainText("Server snapshot");
  await page.locator('[data-ui-action="check-config-updates"]').click();
  await expect(lab).toContainText(/Update available|Current/);

  await draft.locator('input[name="editDefaultValue"]').fill("second");
  await draft.getByRole("button", { name: "Save draft" }).click();
  await expect(row).toContainText("draft 3");
  await page.locator('[data-ui-action="validate-config-draft"]').click();
  await page.locator('[data-ui-action="publish-config-entry"]').click();
  await expect(row).toContainText("published 2");
  const firstRevision = revisions.getByText("Revision 1", { exact: true }).locator("../..");
  await firstRevision.locator('[data-ui-action="rollback-config-entry"]').click();
  await expect(row).toContainText("published 3");

  await expect(page.locator('[data-ui-action="list-config-snapshots"]')).toContainText(
    "Snapshot",
  );
  await list.getByLabel("Include archived configuration").check();
  await row.locator('[data-ui-action="archive-config-entry"]').click();
  await expect(row).toContainText("Archived");
  await row.locator('[data-ui-action="restore-config-entry"]').click();
  await expect(row).toContainText("Active");
});

async function createSegment(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
  segmentKey: string,
) {
  await page.getByRole("link", { name: "Targeting" }).click();
  await page
    .getByLabel("Targeting tenant")
    .selectOption({ label: `Config E2E Tenant (${tenantSlug})` });
  await page
    .getByLabel("Targeting application")
    .selectOption({ label: `Config E2E App (${applicationSlug})` });
  await page
    .getByLabel("Targeting environment")
    .selectOption({ label: `Config E2E Environment (${environmentSlug})` });
  const create = page.locator('[data-ui-action="create-segment"]');
  await create.locator('input[name="segmentKey"]').fill(segmentKey);
  await create.locator('input[name="segmentDisplayName"]').fill("Config users");
  await create.locator('textarea[name="segmentDescription"]').fill("Users in CN");
  await create.locator('input[name="createConditionAttribute"]').fill("region");
  await create.locator('input[name="createConditionValue"]').fill("cn");
  await create.getByRole("button", { name: "Create segment" }).click();
  await expect(page.getByTestId(`targeting-segment-${segmentKey}`)).toBeVisible();
}

async function createScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .first()
    .fill("Config E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("Config E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const form = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await form.getByLabel("Slug").fill(environmentSlug);
  await form.getByLabel("Display name").fill("Config E2E Environment");
  await form.getByLabel("Type").selectOption("Development");
  await form.locator('[data-ui-action="create-environment"]').click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
