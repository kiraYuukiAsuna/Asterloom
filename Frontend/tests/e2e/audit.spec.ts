import { expect, test, type Page } from "@playwright/test";

test("searches, inspects, correlates, and exports audit events through the Web Console", async ({
  page,
}) => {
  test.setTimeout(90_000);
  page.setDefaultTimeout(10_000);

  await signIn(page, "/tenants");
  await expect(page.locator("[data-platform-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = "audit-e2e-" + suffix;
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .first()
    .fill("Audit E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId("tenant-" + tenantSlug)).toBeVisible();

  await page.getByRole("link", { name: "Audit", exact: true }).click();
  await expect(page).toHaveURL("http://localhost:3000/audit");
  await expect(page.locator("[data-audit-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  const auditCard = page.locator('[data-ui-action="list-audit-events"]');
  await expect(auditCard).toBeVisible();
  await auditCard.getByLabel("Operation").fill("CreateTenant");
  await auditCard.getByLabel("Outcome").selectOption("AUDIT_OUTCOME_SUCCEEDED");
  await auditCard.getByRole("button", { name: "Apply filters" }).click();

  const eventRow = auditCard.locator("tbody tr").filter({ hasText: "CreateTenant" }).first();
  await expect(eventRow).toContainText("Succeeded");
  await eventRow.locator('[data-ui-action="get-audit-event"]').click();

  const detail = page.getByTestId("audit-event-detail");
  await expect(detail).toContainText("PlatformAdminService/CreateTenant");
  await expect(detail).toContainText("request_fields=[slug,display_name]");
  const requestId = await detail.locator("code").first().innerText();
  expect(requestId.length).toBeGreaterThanOrEqual(8);

  await detail.getByRole("button", { name: "Show correlated events" }).click();
  await expect(auditCard.getByLabel("Request ID")).toHaveValue(requestId);
  await expect(auditCard.locator("tbody tr")).toHaveCount(1);

  const downloadPromise = page.waitForEvent("download");
  await auditCard.locator('[data-ui-action="export-audit-events"]').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^asterloom-audit-\d{8}-\d{6}\.csv$/);
  await expect(page.getByText(/Exported 1 audit event/)).toBeVisible();
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
