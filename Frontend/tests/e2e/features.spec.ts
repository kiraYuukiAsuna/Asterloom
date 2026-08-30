import { expect, test, type Page } from "@playwright/test";

import { signIn, webUrl } from "./support/environment";

test("manages feature flags through every admin and runtime API", async ({
  page,
}) => {
  test.setTimeout(150_000);
  page.setDefaultTimeout(15_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/tenants");

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `feature-tenant-${suffix}`;
  const applicationSlug = `feature-app-${suffix}`;
  const environmentSlug = `feature-env-${suffix}`;
  const segmentKey = `feature-users-${suffix}`;
  const flagKey = `new-home-${suffix}`;

  await createFeatureScope(page, tenantSlug, applicationSlug, environmentSlug);
  await createFeatureSegment(
    page,
    tenantSlug,
    applicationSlug,
    environmentSlug,
    segmentKey,
  );

  await page.getByRole("link", { name: "Feature flags" }).click();
  await expect(page).toHaveURL(webUrl("/features"));
  await expect(page.locator("[data-feature-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  await page
    .getByLabel("Feature tenant")
    .selectOption({ label: `Feature E2E Tenant (${tenantSlug})` });
  await page
    .getByLabel("Feature application")
    .selectOption({ label: `Feature E2E App (${applicationSlug})` });
  await page
    .getByLabel("Feature environment")
    .selectOption({ label: `Feature E2E Environment (${environmentSlug})` });

  const list = page.locator('[data-ui-action="list-flags"]');
  await expect(list).toBeVisible();
  const create = page.locator('[data-ui-action="create-flag"]');
  await create.locator('input[name="createFlagKey"]').fill(flagKey);
  await create
    .locator('input[name="createFlagDisplayName"]')
    .fill("New home experience");
  await create
    .locator('textarea[name="createFlagDescription"]')
    .fill("Complete Feature browser flow");
  await create.getByRole("button", { name: "Add segment rule" }).click();
  await create
    .locator('select[name="createTargetingSegment"]')
    .selectOption({ label: `Feature users (${segmentKey})` });
  await create
    .locator('select[name="createTargetingVariantKey"]')
    .selectOption("on");
  await create.getByRole("button", { name: "Create flag" }).click();

  const flagRow = page.getByTestId(`feature-flag-${flagKey}`);
  await expect(flagRow).toContainText("New home experience");
  await Promise.all([
    page.waitForResponse(
      (response) =>
        response.request().method() === "GET" &&
        /\/flags\/[0-9a-f-]+(?:\?|$)/i.test(response.url()),
    ),
    flagRow.locator('[data-ui-action="get-flag"]').click(),
  ]);

  const draft = page.locator('[data-ui-action="update-flag-draft"]');
  await expect(draft).toBeVisible();
  await draft
    .locator('input[name="editFlagDisplayName"]')
    .fill("New home experience updated");
  await draft.getByRole("button", { name: "Save draft" }).click();
  await expect(flagRow).toContainText("New home experience updated");

  await page.locator('[data-ui-action="validate-flag-draft"]').click();
  await expect(page.getByText("Draft is publishable")).toBeVisible();
  await page.locator('[data-ui-action="publish-flag"]').click();
  await expect(flagRow).toContainText("published 1");

  const revisions = page.locator('[data-ui-action="list-flag-revisions"]');
  await expect(revisions).toContainText("Revision 1");
  const evaluationCard = page
    .getByRole("heading", { name: "Evaluation lab" })
    .locator("../..");
  await evaluationCard
    .locator('input[name="featureTargetingKey"]')
    .fill(`user-${suffix}`);
  await evaluationCard.locator('input[name="featureRegion"]').fill("cn");
  await page.locator('[data-ui-action="simulate-flag"]').click();
  await expect(evaluationCard).toContainText("Published simulation");
  await expect(evaluationCard).toContainText("on");
  await page.locator('[data-ui-action="evaluate-flag"]').click();
  await expect(evaluationCard).toContainText("Runtime endpoint");

  await draft.getByLabel("Flag enabled").uncheck();
  await draft.getByRole("button", { name: "Save draft" }).click();
  await page.locator('[data-ui-action="validate-flag-draft"]').click();
  await expect(page.getByText("Draft is publishable")).toBeVisible();
  await page.locator('[data-ui-action="publish-flag"]').click();
  await expect(flagRow).toContainText("published 2");

  const firstRevision = revisions
    .getByText("Revision 1", { exact: true })
    .locator("../..");
  await firstRevision.locator('[data-ui-action="rollback-flag"]').click();
  await expect(flagRow).toContainText("published 3");

  await list.getByLabel("Include archived flags").check();
  await flagRow.locator('[data-ui-action="archive-flag"]').click();
  await expect(flagRow).toContainText("Archived");
  await flagRow.locator('[data-ui-action="restore-flag"]').click();
  await expect(flagRow).toContainText("Active");
});

async function createFeatureSegment(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
  segmentKey: string,
) {
  await page.getByRole("link", { name: "Targeting" }).click();
  await expect(page).toHaveURL(webUrl("/targeting/segments"));
  await page
    .getByLabel("Targeting tenant")
    .selectOption({ label: `Feature E2E Tenant (${tenantSlug})` });
  await page
    .getByLabel("Targeting application")
    .selectOption({ label: `Feature E2E App (${applicationSlug})` });
  await page
    .getByLabel("Targeting environment")
    .selectOption({ label: `Feature E2E Environment (${environmentSlug})` });
  const create = page.locator('[data-ui-action="create-segment"]');
  await create.locator('input[name="segmentKey"]').fill(segmentKey);
  await create
    .locator('input[name="segmentDisplayName"]')
    .fill("Feature users");
  await create
    .locator('textarea[name="segmentDescription"]')
    .fill("Users in the CN region");
  await create.locator('input[name="createConditionAttribute"]').fill("region");
  await create.locator('input[name="createConditionValue"]').fill("cn");
  await create.getByRole("button", { name: "Create segment" }).click();
  await expect(page.getByTestId(`targeting-segment-${segmentKey}`)).toBeVisible();
}

async function createFeatureScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  await expect(page.locator('[data-ui-action="list-tenants"]')).toBeVisible();
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .first()
    .fill("Feature E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await expect(page.locator('[data-ui-action="list-applications"]')).toBeVisible();
  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("Feature E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const createEnvironmentForm = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await createEnvironmentForm.getByLabel("Slug").fill(environmentSlug);
  await createEnvironmentForm
    .getByLabel("Display name")
    .fill("Feature E2E Environment");
  await createEnvironmentForm.getByLabel("Type").selectOption("Development");
  await createEnvironmentForm
    .locator('[data-ui-action="create-environment"]')
    .click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
