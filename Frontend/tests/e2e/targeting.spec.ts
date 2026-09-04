import { expect, test, type Page } from "@playwright/test";

import {
  signIn,
  skipApplicationInitialization,
  webUrl,
} from "./support/environment";

test("manages and simulates targeting segments through every admin API", async ({
  page,
}) => {
  test.setTimeout(120_000);
  page.setDefaultTimeout(12_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/tenants");

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `targeting-tenant-${suffix}`;
  const applicationSlug = `targeting-app-${suffix}`;
  const environmentSlug = `targeting-env-${suffix}`;
  const segmentKey = `china-preview-${suffix}`;

  await createTargetingScope(
    page,
    tenantSlug,
    applicationSlug,
    environmentSlug,
  );

  await page.getByRole("link", { name: "Targeting" }).click();
  await expect(page).toHaveURL(webUrl("/targeting/segments"));
  await expect(page.locator("[data-targeting-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  await expect(
    page.locator('[data-ui-action="list-targeting-attributes"]'),
  ).toBeVisible();

  await page
    .getByLabel("Targeting tenant")
    .fill(`Targeting E2E Tenant (${tenantSlug})`);
  await page
    .getByLabel("Targeting application")
    .fill(`Targeting E2E App (${applicationSlug})`);
  await page
    .getByLabel("Targeting environment")
    .fill(`Targeting E2E Environment (${environmentSlug})`);

  const segmentList = page.locator('[data-ui-action="list-segments"]');
  await expect(segmentList).toBeVisible();

  const createForm = page.locator('[data-ui-action="create-segment"]');
  await createForm.locator('input[name="segmentKey"]').fill(segmentKey);
  await createForm
    .locator('input[name="segmentDisplayName"]')
    .fill("China preview audience");
  await createForm
    .locator('textarea[name="segmentDescription"]')
    .fill("Users whose region is China");
  await createForm
    .locator('select[name="createConditionAttribute"]')
    .selectOption("region");
  await createForm
    .locator('input[name="createConditionValue"]')
    .fill("cn");
  await createForm.getByRole("button", { name: "Create segment" }).click();

  const segmentRow = page.getByTestId(`targeting-segment-${segmentKey}`);
  await expect(segmentRow).toContainText("China preview audience");

  await Promise.all([
    page.waitForResponse(
      (response) =>
        response.request().method() === "GET" &&
        /\/targeting\/segments\/[^/?]+(?:\?|$)/.test(response.url()),
    ),
    segmentRow.locator('[data-ui-action="get-segment"]').click(),
  ]);
  const updateForm = page.locator('[data-ui-action="update-segment"]');
  await expect(updateForm).toBeVisible();
  await updateForm
    .locator('input[name="editSegmentDisplayName"]')
    .fill("China preview audience updated");
  await updateForm
    .locator('textarea[name="editSegmentDescription"]')
    .fill("Updated by the complete Targeting browser flow");
  await updateForm.getByRole("button", { name: "Save segment" }).click();
  await expect(segmentRow).toContainText("China preview audience updated");

  const simulator = page.locator('[data-ui-action="simulate-targeting"]');
  await simulator
    .locator('input[name="simulationTargetingKey"]')
    .fill(`user-${suffix}`);
  await simulator.locator('input[name="simulationRegion"]').fill("cn");
  await simulator
    .getByLabel("Preview deterministic bucket allocation")
    .check();
  await simulator
    .locator('input[name="simulationResourceKey"]')
    .fill(`new-home-${suffix}`);
  await simulator.locator('input[name="simulationSalt"]').fill("e2e-stable-salt");
  await simulator.getByRole("button", { name: "Simulate on server" }).click();
  await expect(simulator).toContainText("Segment matched");
  await expect(simulator).toContainText("selected variant enabled");

  await segmentList.getByLabel("Include archived segments").check();
  await segmentRow.locator('[data-ui-action="archive-segment"]').click();
  await expect(segmentRow).toContainText("Archived");
  await segmentRow.locator('[data-ui-action="restore-segment"]').click();
  await expect(segmentRow).toContainText("Active");
});

async function createTargetingScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  const tenantPanel = page.locator('[data-ui-action="list-tenants"]');
  await expect(tenantPanel).toBeVisible();
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .first()
    .fill("Targeting E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  const applicationPanel = page.locator('[data-ui-action="list-applications"]');
  await expect(applicationPanel).toBeVisible();
  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("Targeting E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await skipApplicationInitialization(page);
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const environmentPanel = page.locator('[data-ui-action="list-environments"]');
  await expect(environmentPanel).toBeVisible();
  const createEnvironmentForm = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await createEnvironmentForm.getByLabel("Slug").fill(environmentSlug);
  await createEnvironmentForm
    .getByLabel("Display name")
    .fill("Targeting E2E Environment");
  await createEnvironmentForm.getByLabel("Type").selectOption("Development");
  await createEnvironmentForm
    .locator('[data-ui-action="create-environment"]')
    .click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
