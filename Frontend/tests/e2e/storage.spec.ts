import { expect, test, type Page } from "@playwright/test";

import { signIn, webUrl } from "./support/environment";

test("manages buckets and verified objects through every Storage admin API", async ({
  page,
}) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(20_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/tenants");
  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `storage-tenant-${suffix}`;
  const applicationSlug = `storage-app-${suffix}`;
  const environmentSlug = `storage-env-${suffix}`;
  const bucketKey = `artifacts-${suffix}`;
  const objectKey = `e2e/${suffix}.txt`;
  const copyKey = `e2e/${suffix}-copy.txt`;

  await createScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page.getByRole("link", { name: "Storage" }).click();
  await expect(page).toHaveURL(webUrl("/storage/objects"));
  await page.getByRole("link", { name: "Buckets", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/storage/buckets"));
  await expect(page.locator("[data-storage-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  await expect(page.locator('[data-ui-action="list-storage-buckets"]')).toBeVisible();
  await page
    .getByLabel("Storage tenant", { exact: true })
    .fill(`Storage E2E Tenant (${tenantSlug})`);

  await page.locator('input[name="bucketKey"]').fill(bucketKey);
  await page.locator('input[name="bucketDisplayName"]').fill("Storage E2E Bucket");
  await page
    .locator('textarea[name="bucketDescription"]')
    .fill("Complete browser coverage for verified object storage");
  await page.locator('input[name="bucketQuotaMiB"]').fill("64");
  await page.locator('input[name="bucketMaxObjectMiB"]').fill("16");
  await page.locator('input[name="bucketContentTypes"]').fill("text/plain");
  await page.locator('[data-ui-action="create-storage-bucket"]').click();

  const bucketRow = page.getByTestId(`storage-bucket-${bucketKey}`);
  await expect(bucketRow).toContainText("Storage E2E Bucket");
  await bucketRow.locator('[data-ui-action="get-storage-bucket"]').click();
  await page
    .locator('input[name="editBucketDisplayName"]')
    .fill("Storage E2E Bucket Updated");
  await page
    .locator('textarea[name="editBucketDescription"]')
    .fill("Updated quota and content policy");
  await page.locator('[data-ui-action="update-storage-bucket"]').click();
  await expect(bucketRow).toContainText("Storage E2E Bucket Updated");

  await page.getByRole("link", { name: "Objects", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/storage/objects"));
  await expect(page.locator('[data-ui-action="list-storage-objects"]')).toBeVisible();
  await page
    .getByLabel("Object bucket")
    .selectOption({ label: `Storage E2E Bucket Updated (${bucketKey})` });
  await page
    .getByLabel("Upload application", { exact: true })
    .fill(`Storage E2E App (${applicationSlug})`);
  await page
    .getByLabel("Upload environment", { exact: true })
    .fill(`Storage E2E Environment (${environmentSlug})`);
  await page.locator('input[name="storageFile"]').setInputFiles({
    buffer: Buffer.from(`Asterloom storage E2E ${suffix}`, "utf8"),
    mimeType: "text/plain",
    name: "artifact.txt",
  });
  await page.locator('input[name="storageObjectKey"]').fill(objectKey);
  await page.locator('textarea[name="uploadMetadata"]').fill("source=e2e");
  await page.locator('[data-ui-action="create-storage-upload-session"]').click();
  await expect(page.getByText("Transfer ticket ready")).toBeVisible();
  await page.locator('[data-ui-action="complete-storage-upload"]').click();

  const originalRow = page.getByTestId(`storage-object-${objectKey}`);
  await expect(originalRow).toContainText("artifact.txt");
  await originalRow.locator('[data-ui-action="get-storage-object"]').click();
  await page.locator('input[name="editObjectFileName"]').fill("artifact-updated.txt");
  await page
    .locator('textarea[name="editObjectMetadata"]')
    .fill("source=e2e\nreviewed=true");
  await page.locator('[data-ui-action="update-storage-object-metadata"]').click();
  await expect(originalRow).toContainText("artifact-updated.txt");

  const downloadPromise = page.waitForEvent("download");
  await page.locator('[data-ui-action="create-storage-download-url"]').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("artifact-updated.txt");

  await page.locator('input[name="copyObjectKey"]').fill(copyKey);
  await page.locator('input[name="copyObjectFileName"]').fill("artifact-copy.txt");
  await page
    .locator('textarea[name="copyObjectMetadata"]')
    .fill("source=e2e\ncopy=true");
  await page.locator('[data-ui-action="copy-storage-object"]').click();
  const copyRow = page.getByTestId(`storage-object-${copyKey}`);
  await expect(copyRow).toContainText("artifact-copy.txt");

  await page.getByLabel("Include deleted storage objects").check();
  await expect(copyRow).toBeVisible();
  await page.locator('[data-ui-action="delete-storage-object"]').click();
  await expect(copyRow).toContainText("deleted", { ignoreCase: true });

  await originalRow.locator('[data-ui-action="get-storage-object"]').click();
  await page.locator('[data-ui-action="delete-storage-object"]').click();
  await expect(originalRow).toContainText("deleted", { ignoreCase: true });

  await page.getByRole("link", { name: "Buckets", exact: true }).click();
  await page.getByLabel("Include archived storage buckets").check();
  await bucketRow.locator('[data-ui-action="get-storage-bucket"]').click();
  await expect(page.locator('[data-ui-action="archive-storage-bucket"]')).toBeEnabled();
  await page.locator('[data-ui-action="archive-storage-bucket"]').click();
  await expect(bucketRow).toContainText("Archived");
  await page.locator('[data-ui-action="restore-storage-bucket"]').click();
  await expect(bucketRow).toContainText("Active");
});

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
    .fill("Storage E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("Storage E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const form = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await form.getByLabel("Slug").fill(environmentSlug);
  await form.getByLabel("Display name").fill("Storage E2E Environment");
  await form.getByLabel("Type").selectOption("Development");
  await form.locator('[data-ui-action="create-environment"]').click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
