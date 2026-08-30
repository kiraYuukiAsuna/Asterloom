import { expect, test, type Page } from "@playwright/test";

import { signIn, webUrl } from "./support/environment";

test("manages every Telemetry API through the Web Console", async ({ page }) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(25_000);

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `telemetry-tenant-${suffix}`;
  const applicationSlug = `telemetry-app-${suffix}`;
  const environmentSlug = `telemetry-env-${suffix}`;
  const sourceKey = `checkout-api-${suffix}`;

  await signIn(page, "/tenants");
  await createScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page.getByLabel("Primary navigation").getByRole("link", { name: "Telemetry", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/telemetry/health"));
  await expect(page.locator("[data-telemetry-workspace]")).toHaveAttribute("data-hydrated", "true");
  await selectScope(page, tenantSlug, applicationSlug, environmentSlug);

  await expect(page.locator('[data-ui-action="get-telemetry-collector-health"]')).toBeVisible();
  await expect(page.getByTestId("telemetry-collector-health")).toBeVisible();
  await expect(page.locator('[data-ui-action="list-telemetry-errors"]')).toBeVisible();

  await page.getByRole("link", { name: "Sources & export", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/telemetry/sources"));
  await expect(page.locator('[data-ui-action="list-telemetry-sources"]')).toBeVisible();
  await expect(page.locator('[data-ui-action="get-telemetry-settings"]')).toBeVisible();

  await page.locator('input[name="telemetrySourceKey"]').fill(sourceKey);
  await page.locator('input[name="telemetrySourceDisplayName"]').fill("Checkout API E2E");
  await page.locator('input[name="telemetrySourceServiceName"]').fill(`asterloom.checkout-${suffix}`);
  await page.locator('input[name="telemetrySourceDescription"]').fill("Telemetry E2E source");
  await page.locator('textarea[name="telemetrySourceAttributes"]').fill(JSON.stringify({ "team.name": "payments", "build.test": true }, null, 2));
  await page.locator('[data-ui-action="create-telemetry-source"]').click();

  const sourceRow = page.getByTestId(`telemetry-source-${sourceKey}`);
  await expect(sourceRow).toBeVisible();
  await sourceRow.locator('[data-ui-action="get-telemetry-source"]').click();
  await page.locator('input[name="editTelemetrySourceDisplayName"]').fill("Checkout API E2E updated");
  await page.locator('textarea[name="editTelemetrySourceDescription"]').fill("Updated Telemetry E2E source");
  await page.locator('[data-ui-action="update-telemetry-source"]').click();
  await expect(sourceRow).toContainText("Checkout API E2E updated");

  await page.getByLabel("Include archived").check();
  await page.locator('[data-ui-action="archive-telemetry-source"]').click();
  await expect(sourceRow).toContainText("archived", { ignoreCase: true });
  await page.locator('[data-ui-action="restore-telemetry-source"]').click();
  await expect(sourceRow).toContainText("active", { ignoreCase: true });

  await page.locator('input[name="telemetrySamplingRatio"]').fill("0.25");
  await page.locator('select[name="telemetryExporterProtocol"]').selectOption("OTLP_PROTOCOL_HTTP_PROTOBUF");
  await page.locator('input[name="telemetryExporterEndpoint"]').fill("http://otel-collector:4318");
  await page.locator('input[name="telemetryDiagnosticsBaseUrl"]').fill("http://localhost:16686/search");
  await page.locator('[data-ui-action="update-telemetry-settings"]').click();
  await expect(page.locator('input[name="telemetrySamplingRatio"]')).toHaveValue("0.25");

  await page.getByRole("link", { name: "Health & errors", exact: true }).click();
  const traceId = "0123456789abcdef0123456789abcdef";
  await page.locator('input[name="telemetryDiagnosticTraceId"]').fill(traceId);
  await page.locator('[data-ui-action="get-telemetry-diagnostic-link"]').click();
  const diagnosticLink = page.getByTestId("telemetry-diagnostic-link");
  await expect(diagnosticLink).toBeVisible();
  await expect(diagnosticLink).toHaveAttribute("href", new RegExp(`traceId=${traceId}`));
});

async function selectScope(page: Page, tenantSlug: string, applicationSlug: string, environmentSlug: string) {
  await page.getByLabel("Telemetry tenant").selectOption({ label: `Telemetry E2E Tenant (${tenantSlug})` });
  await page.getByLabel("Telemetry application").selectOption({ label: `Telemetry E2E App (${applicationSlug})` });
  await page.getByLabel("Telemetry environment").selectOption({ label: `Telemetry E2E Environment (${environmentSlug})` });
}

async function createScope(page: Page, tenantSlug: string, applicationSlug: string, environmentSlug: string) {
  await page.getByLabel("Slug", { exact: true }).first().fill(tenantSlug);
  await page.getByLabel("Display name", { exact: true }).first().fill("Telemetry E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page.getByLabel("Display name", { exact: true }).nth(1).fill("Telemetry E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const form = page.locator("form").filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await form.getByLabel("Slug").fill(environmentSlug);
  await form.getByLabel("Display name").fill("Telemetry E2E Environment");
  await form.getByLabel("Type").selectOption("Development");
  await form.locator('[data-ui-action="create-environment"]').click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
