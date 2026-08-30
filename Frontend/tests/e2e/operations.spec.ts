import { expect, test } from "@playwright/test";

import { signIn, webUrl } from "./support/environment";

test("uses every Operations API through the Web Console", async ({ page }) => {
  test.setTimeout(120_000);
  page.setDefaultTimeout(25_000);

  await signIn(page, "/operations/apis");
  await expect(page).toHaveURL(webUrl("/operations/apis"));
  await expect(page.locator('[data-ui-action="list-operation-apis"]')).toBeVisible();
  await expect(page.getByTestId("operations-api-asterloom.operations.admin.v1.OperationsAdminService-ListApis")).toBeVisible();
  await expect(page.getByText("/api/v1/operations/apis", { exact: true })).toBeVisible();

  const downloadPromise = page.waitForEvent("download");
  await page.locator('[data-ui-action="get-operations-openapi"]').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("asterloom-v1.openapi.json");
  await expect(page.getByTestId("operations-openapi-hash")).toContainText(/^SHA-256 [0-9a-f]{64}$/);

  await page.getByRole("link", { name: "Health", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/operations/health"));
  await expect(page.locator('[data-ui-action="get-operations-health"]')).toBeVisible();
  await expect(page.getByTestId("operations-health")).toBeVisible();
  await expect(page.getByTestId("operations-dependency-self")).toContainText("healthy", { ignoreCase: true });
});
