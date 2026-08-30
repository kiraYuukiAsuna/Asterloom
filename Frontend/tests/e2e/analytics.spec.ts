import { expect, test, type Page } from "@playwright/test";

import { apiUrl, signIn, webUrl } from "./support/environment";

test("manages every Analytics API through the Web Console", async ({ page, request }) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(25_000);

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `analytics-tenant-${suffix}`;
  const applicationSlug = `analytics-app-${suffix}`;
  const environmentSlug = `analytics-env-${suffix}`;
  const eventName = `checkout.completed-${suffix}`;

  await signIn(page, "/tenants");
  await createScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page
    .getByLabel("Primary navigation")
    .getByRole("link", { name: "Analytics", exact: true })
    .click();
  await expect(page).toHaveURL(webUrl("/analytics/explorer"));
  await expect(page.locator("[data-analytics-workspace]")).toHaveAttribute("data-hydrated", "true");
  await selectScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page.getByRole("link", { name: "Schemas & keys", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/analytics/schemas"));
  await expect(page.locator('[data-ui-action="list-analytics-schemas"]')).toBeVisible();
  await expect(page.locator('[data-ui-action="list-analytics-write-keys"]')).toBeVisible();

  await page.locator('input[name="schemaKey"]').fill(eventName);
  await page.locator('input[name="schemaDisplayName"]').fill("Checkout completed E2E");
  await page.locator('input[name="schemaDescription"]').fill("Analytics E2E contract");
  await page.locator('textarea[name="schemaJson"]').fill(
    JSON.stringify(
      {
        type: "object",
        additionalProperties: false,
        required: ["orderId", "amount", "cardToken"],
        properties: {
          orderId: { type: "string" },
          amount: { type: "number" },
          cardToken: { type: "string", "x-asterloom-sensitive": true },
        },
      },
      null,
      2,
    ),
  );
  await page.locator('[data-ui-action="create-analytics-schema"]').click();
  const schemaRow = page.getByTestId(`analytics-schema-${eventName}`);
  await expect(schemaRow).toBeVisible();
  await schemaRow.locator('[data-ui-action="get-analytics-schema"]').click();
  await page.locator('input[name="editAnalyticsSchemaDisplayName"]').fill("Checkout completed E2E updated");
  await page.locator('textarea[name="editAnalyticsSchemaDescription"]').fill("Updated analytics E2E contract");
  await page.locator('[data-ui-action="update-analytics-schema"]').click();
  await expect(schemaRow).toContainText("Checkout completed E2E updated");

  await page.locator('input[name="editAnalyticsRetention"]').fill("120");
  await page.locator('[data-ui-action="update-analytics-retention"]').click();
  await expect(schemaRow).toContainText("120 day retention");
  await page.getByLabel("Include archived analytics schemas").check();
  await page.locator('[data-ui-action="archive-analytics-schema"]').click();
  await expect(schemaRow).toContainText("archived", { ignoreCase: true });
  await page.locator('[data-ui-action="restore-analytics-schema"]').click();
  await expect(schemaRow).toContainText("active", { ignoreCase: true });

  await page.getByPlaceholder("Production .NET SDK").fill("Analytics E2E SDK");
  await page.locator('[data-ui-action="create-analytics-write-key"]').click();
  let secret = (await page.getByTestId("analytics-write-key-secret").textContent())!.trim();
  const writeKeyRow = page.locator('[data-testid^="analytics-write-key-"]').filter({ hasText: "Analytics E2E SDK" });
  await expect(writeKeyRow).toBeVisible();
  await writeKeyRow.locator('[data-ui-action="rotate-analytics-write-key"]').click();
  await expect(page.getByTestId("analytics-write-key-secret")).not.toHaveText(secret);
  secret = (await page.getByTestId("analytics-write-key-secret").textContent())!.trim();

  const eventId = `analytics-e2e-${suffix}`;
  const ingestion = await request.post(apiUrl("/api/v1/analytics/events:batch"), {
    headers: { "X-Asterloom-Write-Key": secret },
    data: {
      events: [
        {
          eventId,
          eventName,
          occurredAt: new Date().toISOString(),
          actorId: `user-${suffix}`,
          sessionId: `session-${suffix}`,
          propertiesJson: JSON.stringify({ orderId: `order-${suffix}`, amount: 42.5, cardToken: "must-redact" }),
          contextJson: JSON.stringify({ platform: "playwright", version: "1.0.0" }),
          sdkName: "playwright-e2e",
          sdkVersion: "1.0.0",
        },
      ],
    },
  });
  const ingestionBody = await ingestion.text();
  expect(ingestion.ok(), ingestionBody).toBeTruthy();
  expect(JSON.parse(ingestionBody).accepted, ingestionBody).toBe(1);

  await page.getByRole("link", { name: "Explorer", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/analytics/explorer"));
  await expect(page.locator('[data-ui-action="list-analytics-events"]')).toBeVisible();
  const eventRow = page.getByTestId(`analytics-event-${eventId}`);
  await expect(eventRow).toBeVisible();
  await eventRow.locator('[data-ui-action="get-analytics-event"]').click();
  await expect(page.getByText("[REDACTED]", { exact: false })).toBeVisible();

  await page.locator('input[name="queryEventNames"]').fill(eventName);
  await page.locator('[data-ui-action="query-analytics-aggregation"]').click();
  await expect(page.getByTestId("analytics-query-results")).toContainText(eventName);

  await page.locator('input[name="exportEventName"]').fill(eventName);
  const downloadPromise = page.waitForEvent("download");
  await page.locator('[data-ui-action="export-analytics-events"]').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^asterloom-analytics-.*\.csv$/);

  await page.getByRole("link", { name: "Schemas & keys", exact: true }).click();
  await writeKeyRow.locator('[data-ui-action="revoke-analytics-write-key"]').click();
  await expect(writeKeyRow).toContainText("revoked", { ignoreCase: true });
});

async function selectScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  await page.getByLabel("Analytics tenant").selectOption({ label: `Analytics E2E Tenant (${tenantSlug})` });
  await page.getByLabel("Analytics application").selectOption({ label: `Analytics E2E App (${applicationSlug})` });
  await page.getByLabel("Analytics environment").selectOption({ label: `Analytics E2E Environment (${environmentSlug})` });
}

async function createScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page.getByLabel("Display name", { exact: true }).first().fill("Analytics E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page.getByLabel("Display name", { exact: true }).nth(1).fill("Analytics E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const form = page.locator("form").filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await form.getByLabel("Slug").fill(environmentSlug);
  await form.getByLabel("Display name").fill("Analytics E2E Environment");
  await form.getByLabel("Type").selectOption("Development");
  await form.locator('[data-ui-action="create-environment"]').click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
