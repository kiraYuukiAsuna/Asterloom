import {
  constants,
  createHash,
  createPublicKey,
  generateKeyPairSync,
  sign,
} from "node:crypto";

import { expect, test, type Page } from "@playwright/test";
import { strToU8, zipSync } from "fflate";

import { signIn, webUrl } from "./support/environment";

test("manages every signed Release API through the Web Console", async ({ page }) => {
  test.setTimeout(240_000);
  page.setDefaultTimeout(25_000);

  const suffix = Date.now().toString(36).slice(-8);
  const tenantSlug = `release-tenant-${suffix}`;
  const applicationSlug = `release-app-${suffix}`;
  const environmentSlug = `release-env-${suffix}`;
  const channelKey = `stable-${suffix}`;
  const unusedChannelKey = `canary-${suffix}`;
  const signingKey = `desktop-e2e-${suffix}`;
  const { privateKey, publicKey } = generateKeyPairSync("rsa", {
    modulusLength: 2048,
    privateKeyEncoding: { format: "pem", type: "pkcs8" },
    publicKeyEncoding: { format: "pem", type: "spki" },
  });

  await signIn(page, "/tenants");
  await createScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page
    .getByLabel("Primary navigation")
    .getByRole("link", { name: "Releases", exact: true })
    .click();
  await expect(page).toHaveURL(webUrl("/releases"));
  await expect(page.locator("[data-release-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  await selectScope(page, tenantSlug, applicationSlug, environmentSlug);

  await page.getByRole("link", { name: "Channels", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/channels"));
  await expect(page.locator('[data-ui-action="list-release-channels"]')).toBeVisible();

  await createChannel(page, channelKey, "Stable E2E");
  const stableChannel = page.getByTestId(`release-channel-${channelKey}`);
  await stableChannel.locator('[data-ui-action="get-release-channel"]').click();
  await page
    .locator('input[name="editChannelDisplayName"]')
    .fill("Stable E2E Updated");
  await page
    .locator('textarea[name="editChannelDescription"]')
    .fill("Updated signed desktop channel");
  await page.locator('[data-ui-action="update-release-channel"]').click();
  await expect(stableChannel).toContainText("Stable E2E Updated");

  await createChannel(page, unusedChannelKey, "Canary E2E");
  const unusedChannel = page.getByTestId(`release-channel-${unusedChannelKey}`);
  await page.getByLabel("Include archived release channels").check();
  await unusedChannel.locator('[data-ui-action="get-release-channel"]').click();
  await page.locator('[data-ui-action="archive-release-channel"]').click();
  await expect(unusedChannel).toContainText("archived", { ignoreCase: true });
  await page.locator('[data-ui-action="restore-release-channel"]').click();
  await expect(unusedChannel).toContainText("active", { ignoreCase: true });

  await page.getByRole("link", { name: "Artifacts & keys", exact: true }).click();
  await expect(page).toHaveURL(webUrl("/artifacts"));
  await expect(
    page.locator('[data-ui-action="list-release-signing-keys"]'),
  ).toBeVisible();
  await expect(page.locator('[data-ui-action="list-release-artifacts"]')).toBeVisible();
  await page.locator('input[name="signingKey"]').fill(signingKey);
  await page.locator('input[name="signingKeyDisplayName"]').fill("Release E2E Key");
  await page.locator('textarea[name="publicKeyPem"]').fill(publicKey);
  await page.locator('[data-ui-action="create-release-signing-key"]').click();
  const keyRow = page.getByTestId(`release-signing-key-${signingKey}`);
  await expect(keyRow).toContainText("Release E2E Key");
  await page.getByLabel("Include archived release signing keys").check();
  await keyRow.locator('[data-ui-action="archive-release-signing-key"]').click();
  await expect(keyRow).toContainText("archived", { ignoreCase: true });
  await keyRow.locator('[data-ui-action="restore-release-signing-key"]').click();
  await expect(keyRow).toContainText("active", { ignoreCase: true });

  const unusedArtifact = await quickUploadVelopackArtifact(
    page,
    "9.9.9",
    `Asterloom.E2E.${suffix}`,
    privateKey,
    publicKey,
  );
  await page.getByLabel("Include archived release artifacts").check();
  await unusedArtifact.locator('[data-ui-action="get-release-artifact"]').click();
  await page.locator('[data-ui-action="archive-release-artifact"]').click();
  await expect(unusedArtifact).toContainText("archived", { ignoreCase: true });

  const firstArtifact = await uploadArtifact(
    page,
    "1.0.0",
    `asterloom-${suffix}-1.0.0.bin`,
    Buffer.from(`Asterloom desktop 1.0.0 ${suffix}`, "utf8"),
    privateKey,
  );
  await firstArtifact.locator('[data-ui-action="get-release-artifact"]').click();

  await page
    .getByLabel("Release views")
    .getByRole("link", { name: "Releases", exact: true })
    .click();
  await expect(page).toHaveURL(webUrl("/releases"));
  await expect(page.locator('[data-ui-action="list-desktop-releases"]')).toBeVisible();
  await createReleaseDraft(
    page,
    channelKey,
    "1.0.0",
    "Desktop 1.0.0",
    `asterloom-${suffix}-1.0.0.bin`,
    "50000",
  );
  const firstRelease = page.getByTestId(`desktop-release-1.0.0-${channelKey}`);
  await firstRelease.locator('[data-ui-action="get-desktop-release"]').click();
  await page
    .locator('textarea[name="editReleaseNotes"]')
    .fill("Updated E2E release notes");
  await page.locator('[data-ui-action="update-desktop-release-draft"]').click();
  await validateAndPublish(page, privateKey, signingKey);
  await expect(firstRelease).toContainText("published", { ignoreCase: true });
  await page.locator('[data-ui-action="get-release-manifest"]').click();
  await expect(page.getByText(`${channelKey}/1.0.0`)).toBeVisible();

  await page.locator('[data-ui-action="pause-desktop-release"]').click();
  await expect(firstRelease).toContainText("paused", { ignoreCase: true });
  await page.locator('input[name="promotionRolloutBasisPoints"]').fill("100000");
  await page.locator('[data-ui-action="promote-desktop-release"]').click();
  await expect(firstRelease).toContainText("published", { ignoreCase: true });

  await page.locator('input[name="simulationChannelKey"]').fill(channelKey);
  await page.locator('input[name="simulationCurrentVersion"]').fill("0.9.0");
  await page.locator('input[name="simulationTargetingKey"]').fill(`user-${suffix}`);
  await page.locator('[data-ui-action="simulate-release-update"]').click();
  await expect(page.getByText("Update available", { exact: true })).toBeVisible();

  await page.getByRole("link", { name: "Artifacts & keys", exact: true }).click();
  await uploadArtifact(
    page,
    "2.0.0",
    `asterloom-${suffix}-2.0.0.bin`,
    Buffer.from(`Asterloom desktop 2.0.0 ${suffix}`, "utf8"),
    privateKey,
  );

  await page
    .getByLabel("Release views")
    .getByRole("link", { name: "Releases", exact: true })
    .click();
  await createReleaseDraft(
    page,
    channelKey,
    "2.0.0",
    "Desktop 2.0.0",
    `asterloom-${suffix}-2.0.0.bin`,
    "100000",
  );
  const secondRelease = page.getByTestId(`desktop-release-2.0.0-${channelKey}`);
  await secondRelease.locator('[data-ui-action="get-desktop-release"]').click();
  await validateAndPublish(page, privateKey, signingKey);
  await expect(secondRelease).toContainText("published", { ignoreCase: true });

  await page.getByLabel("Include inactive desktop releases").check();
  await page
    .locator('select[name="rollbackTargetRelease"]')
    .selectOption({ label: "1.0.0 · Desktop 1.0.0" });
  await page.locator('[data-ui-action="rollback-desktop-release"]').click();
  await expect(secondRelease).toContainText("rolled back", { ignoreCase: true });
});

async function createChannel(page: Page, key: string, displayName: string) {
  await page.locator('input[name="channelKey"]').fill(key);
  await page.locator('input[name="channelDisplayName"]').fill(displayName);
  await page
    .locator('textarea[name="channelDescription"]')
    .fill(`Release channel ${displayName}`);
  await page.locator('[data-ui-action="create-release-channel"]').click();
  await expect(page.getByTestId(`release-channel-${key}`)).toBeVisible();
}

async function uploadArtifact(
  page: Page,
  version: string,
  fileName: string,
  buffer: Buffer,
  privateKey: string,
) {
  await page.locator('[data-ui-action="select-advanced-artifact-upload"]').click();
  await page.locator('input[name="releaseArtifactFile"]').setInputFiles({
    buffer,
    mimeType: "application/octet-stream",
    name: fileName,
  });
  const shaOutput = page.getByTestId("artifact-sha256");
  await expect(shaOutput).toHaveText(/^[a-f0-9]{64}$/);
  const sha256 = (await shaOutput.textContent())!;
  await page.locator('input[name="artifactReleaseVersion"]').fill(version);
  await page.locator('input[name="artifactRuntimeId"]').fill("win-x64");
  await page.locator('select[name="artifactKind"]').selectOption({ label: "Full package" });
  await page
    .locator('select[name="artifactSigningKey"]')
    .selectOption({ index: 1 });
  await page
    .locator('textarea[name="artifactSignature"]')
    .fill(signDigest(privateKey, sha256));
  await page.locator('[data-ui-action="create-release-artifact-upload"]').click();
  await expect(page.getByText("Upload ticket ready")).toBeVisible();
  await page.locator('[data-ui-action="complete-release-artifact-upload"]').click();
  const row = page.locator(
    `[data-testid^="release-artifact-${version}-win-x64-"]`,
  );
  await expect(row).toContainText(fileName);
  await expect(row).toContainText("verified", { ignoreCase: true });
  return row;
}

async function quickUploadVelopackArtifact(
  page: Page,
  version: string,
  packageId: string,
  privateKey: string,
  publicKey: string,
) {
  const fileName = `${packageId}-${version}-stable-full.nupkg`;
  const packageBytes = Buffer.from(
    zipSync({
      [`${packageId}.nuspec`]: strToU8(`<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>${packageId}</id>
    <version>${version}</version>
    <authors>Asterloom</authors>
    <description>Release Web E2E package</description>
    <channel>stable</channel>
    <rid>win-x64</rid>
  </metadata>
</package>`),
    }),
  );
  const sha256 = createHash("sha256").update(packageBytes).digest("hex");
  const fingerprint = createHash("sha256")
    .update(
      createPublicKey(publicKey).export({
        format: "der",
        type: "spki",
      }),
    )
    .digest("hex");
  const signingBundle = Buffer.from(
    JSON.stringify({
      algorithm: "RSA-PSS-SHA256",
      artifacts: {
        [fileName]: {
          sha256,
          signature: signDigest(privateKey, sha256),
        },
      },
      fingerprint,
    }),
    "utf8",
  );

  await page.locator('input[name="velopackPackages"]').setInputFiles({
    buffer: packageBytes,
    mimeType: "application/octet-stream",
    name: fileName,
  });
  await page.locator('input[name="velopackSigningBundle"]').setInputFiles({
    buffer: signingBundle,
    mimeType: "application/json",
    name: "signing-metadata.json",
  });
  const packageRow = page.getByTestId(`velopack-package-${fileName}`);
  await expect(packageRow).toContainText("Ready");
  await page.locator('[data-ui-action="upload-velopack-packages"]').click();
  await expect(packageRow).toContainText("Verified");

  const row = page.locator(
    `[data-testid^="release-artifact-${version}-win-x64-"]`,
  );
  await expect(row).toContainText(fileName);
  await expect(row).toContainText("verified", { ignoreCase: true });
  return row;
}

async function createReleaseDraft(
  page: Page,
  channelKey: string,
  version: string,
  displayName: string,
  artifactFileName: string,
  rolloutBasisPoints: string,
) {
  await page
    .locator('select[name="releaseChannel"]')
    .selectOption({ label: `Stable E2E Updated (${channelKey})` });
  await page.locator('input[name="releaseVersion"]').fill(version);
  await page.locator('input[name="releaseDisplayName"]').fill(displayName);
  await page.locator('input[name="releaseMinimumVersion"]').fill("0.0.0");
  await page
    .locator('input[name="releaseRolloutBasisPoints"]')
    .fill(rolloutBasisPoints);
  await page
    .getByLabel(`Create artifact ${artifactFileName}`, { exact: true })
    .check();
  await page.locator('[data-ui-action="create-desktop-release"]').click();
  await expect(page.getByTestId(`desktop-release-${version}-${channelKey}`)).toBeVisible();
}

async function validateAndPublish(page: Page, privateKey: string, signingKey: string) {
  await page.locator('[data-ui-action="validate-desktop-release"]').click();
  const hash = page.getByTestId("manifest-sha256");
  await expect(hash).toHaveText(/^[a-f0-9]{64}$/);
  const sha256 = (await hash.textContent())!;
  await page
    .locator('select[name="manifestSigningKey"]')
    .selectOption({ label: `Release E2E Key (${signingKey})` });
  await page
    .locator('textarea[name="manifestSignature"]')
    .fill(signDigest(privateKey, sha256));
  await page.locator('[data-ui-action="publish-desktop-release"]').click();
}

function signDigest(privateKey: string, digest: string) {
  return sign("sha256", Buffer.from(digest, "utf8"), {
    key: privateKey,
    padding: constants.RSA_PKCS1_PSS_PADDING,
    saltLength: 32,
  }).toString("base64");
}

async function selectScope(
  page: Page,
  tenantSlug: string,
  applicationSlug: string,
  environmentSlug: string,
) {
  await page
    .getByLabel("Release tenant")
    .fill(`Release E2E Tenant (${tenantSlug})`);
  await page
    .getByLabel("Release application")
    .fill(`Release E2E App (${applicationSlug})`);
  await page
    .getByLabel("Release environment")
    .fill(`Release E2E Environment (${environmentSlug})`);
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
    .fill("Release E2E Tenant");
  await page.locator('[data-ui-action="create-tenant"]').click();
  await expect(page.getByTestId(`tenant-${tenantSlug}`)).toBeVisible();

  await page.getByLabel("Slug", { exact: true }).nth(1).fill(applicationSlug);
  await page
    .getByLabel("Display name", { exact: true })
    .nth(1)
    .fill("Release E2E App");
  await page.locator('[data-ui-action="create-application"]').click();
  await expect(page.getByTestId(`application-${applicationSlug}`)).toBeVisible();

  const form = page
    .locator("form")
    .filter({ has: page.locator('[data-ui-action="create-environment"]') });
  await form.getByLabel("Slug").fill(environmentSlug);
  await form.getByLabel("Display name").fill("Release E2E Environment");
  await form.getByLabel("Type").selectOption("Development");
  await form.locator('[data-ui-action="create-environment"]').click();
  await expect(page.getByTestId(`environment-${environmentSlug}`)).toBeVisible();
}
