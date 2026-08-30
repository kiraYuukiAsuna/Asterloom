import { expect, test, type Browser, type Page } from "@playwright/test";

test("manages the complete Identity surface through the Web Console", async ({ browser, page }) => {
  test.setTimeout(120_000);
  page.setDefaultTimeout(12_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/identity/users");
  await expect(page).toHaveURL("http://localhost:3000/identity/users");
  await expect(page.locator("[data-identity-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  const suffix = Date.now().toString(36).slice(-8);
  const email = `identity-e2e-${suffix}@asterloom.test`;
  const clientId = `identity-e2e-${suffix}`;
  const scopeName = `identity.e2e.${suffix}`;

  const invite = page.locator('[data-ui-action="invite-user"]');
  await invite.getByLabel("Email").fill(email);
  await invite.getByLabel("Display name").fill("Identity E2E User");
  await invite.getByLabel("Developer").check();
  await invite.getByRole("button", { name: "Send invitation" }).click();

  const reveal = page.getByTestId("identity-credential-reveal");
  await expect(reveal).toContainText(email);
  await reveal.getByRole("button", { name: "Dismiss" }).click();

  const userRow = page
    .locator('[data-ui-action="get-user"]')
    .filter({ hasText: email });
  await userRow.click();
  const pendingUserEditor = page.locator('[data-ui-action="update-user"]');
  await expect(pendingUserEditor).toContainText("pending", { ignoreCase: true });
  await pendingUserEditor.locator('[data-ui-action="resend-user-invitation"]').click();
  await expect(reveal).toContainText(email);
  const invitationUrl = await reveal.locator("code").innerText();
  const invitationPage = await page.context().newPage();
  await invitationPage.goto(invitationUrl);
  await invitationPage.locator('input[name="Password"]').fill("Identity-E2E-User!2026");
  await invitationPage
    .locator('input[name="ConfirmPassword"]')
    .fill("Identity-E2E-User!2026");
  await invitationPage.getByRole("button", { name: "激活账户" }).click();
  await expect(invitationPage.getByText("账户已激活", { exact: true })).toBeVisible();
  await invitationPage.close();
  await reveal.getByRole("button", { name: "Dismiss" }).click();

  await establishUserSession(browser, email, "Identity-E2E-User!2026");
  await page.reload();
  await expect(page.locator("[data-identity-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  await expect(page.locator('[data-ui-action="list-users"]')).toBeVisible();
  await userRow.click();
  const userEditor = page.locator('[data-ui-action="update-user"]');
  await expect(userEditor).toContainText("active", { ignoreCase: true });
  await userEditor.getByLabel("Display name").fill("Identity E2E User Updated");
  await userEditor.getByRole("button", { name: "Save profile" }).click();
  await expect(userEditor).toContainText("Identity E2E User Updated");
  await userEditor.getByLabel("Viewer").uncheck();
  await userEditor
    .locator('[data-ui-action="set-user-roles"]')
    .getByRole("button", { name: "Save roles" })
    .click();

  const sessions = page.locator('[data-ui-action="list-user-sessions"]');
  await expect(sessions.locator('[data-ui-action="revoke-user-session"]')).toHaveCount(1);
  await sessions.locator('[data-ui-action="revoke-user-session"]').click();
  await expect(sessions.locator('[data-ui-action="revoke-user-session"]')).toHaveCount(0);

  await establishUserSession(browser, email, "Identity-E2E-User!2026");
  await page.reload();
  await expect(page.locator("[data-identity-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );
  await userRow.click();
  await expect(sessions.locator('[data-ui-action="revoke-user-session"]')).toHaveCount(1);
  await sessions.locator('[data-ui-action="revoke-all-user-sessions"]').click();
  await expect(sessions.locator('[data-ui-action="revoke-user-session"]')).toHaveCount(0);

  await userEditor.locator('[data-ui-action="suspend-user"]').click();
  await expect(userEditor.locator('[data-ui-action="reactivate-user"]')).toBeVisible();
  await userEditor.locator('[data-ui-action="reactivate-user"]').click();
  await userEditor.locator('[data-ui-action="archive-user"]').click();
  await expect(userEditor.locator('[data-ui-action="restore-user"]')).toBeVisible();
  await userEditor.locator('[data-ui-action="restore-user"]').click();

  await page.getByTestId("identity-tab-clients").click();
  const createClient = page.locator('[data-ui-action="create-client"]');
  await createClient.getByLabel("Client ID").fill(clientId);
  await createClient.getByLabel("Display name").fill("Identity E2E Client");
  await createClient.getByLabel("Client type").selectOption("OIDC_CLIENT_TYPE_CONFIDENTIAL");
  await createClient.getByLabel("Authorization code + PKCE").uncheck();
  await createClient.getByLabel("Refresh token").uncheck();
  await createClient.getByLabel("Client credentials").check();
  await createClient.getByLabel("Scopes (comma separated)").fill("asterloom.api");
  await createClient.getByRole("button", { name: "Register client" }).click();
  await expect(page.getByTestId("identity-credential-reveal")).toContainText(clientId);
  await page
    .getByTestId("identity-credential-reveal")
    .getByRole("button", { name: "Dismiss" })
    .click();

  await expect(page.locator('[data-ui-action="list-clients"]')).toBeVisible();
  const clientRow = page
    .locator('[data-ui-action="get-client"]')
    .filter({ hasText: clientId });
  await clientRow.click();
  const clientEditor = page.locator('[data-ui-action="update-client"]');
  await clientEditor.getByLabel("Display name").fill("Identity E2E Client Updated");
  await clientEditor.getByRole("button", { name: "Save client" }).click();
  await expect(clientEditor).toContainText("Identity E2E Client Updated");
  await clientEditor.locator('[data-ui-action="rotate-client-secret"]').click();
  await expect(page.getByTestId("identity-credential-reveal")).toContainText("New secret");
  await page
    .getByTestId("identity-credential-reveal")
    .getByRole("button", { name: "Dismiss" })
    .click();
  await clientEditor.locator('[data-ui-action="delete-client"]').click();
  await expect(clientRow).toHaveCount(0);

  await page.getByTestId("identity-tab-scopes").click();
  const createScope = page.locator('[data-ui-action="create-scope"]');
  await createScope.getByLabel("Scope name").fill(scopeName);
  await createScope.getByLabel("Display name").fill("Identity E2E Scope");
  await createScope.getByLabel("Description").fill("Created by the browser contract.");
  await createScope.getByLabel("Resources (comma separated)").fill("identity-e2e-api");
  await createScope.getByRole("button", { name: "Create scope" }).click();

  await expect(page.locator('[data-ui-action="list-scopes"]')).toBeVisible();
  const scopeRow = page
    .locator('[data-ui-action="get-scope"]')
    .filter({ hasText: scopeName });
  await scopeRow.click();
  const scopeEditor = page.locator('[data-ui-action="update-scope"]');
  await scopeEditor.getByLabel("Display name").fill("Identity E2E Scope Updated");
  await scopeEditor.getByRole("button", { name: "Save scope" }).click();
  await expect(scopeEditor).toContainText("Identity E2E Scope Updated");
  await scopeEditor.locator('[data-ui-action="delete-scope"]').click();
  await expect(scopeRow).toHaveCount(0);
});

async function establishUserSession(browser: Browser, email: string, password: string) {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await signIn(page, "/", email, password);
    await expect(page).toHaveURL("http://localhost:3000/");
    const session = await page.request.get("/api/auth/session");
    expect(session.ok()).toBeTruthy();
    expect((await session.json()).actor.email).toBe(email);
  } finally {
    await context.close();
  }
}

async function signIn(
  page: Page,
  returnTo: string,
  email = "admin@asterloom.test",
  password = "Asterloom-E2E-Admin!2026",
) {
  await page.goto(returnTo);
  await expect(page).toHaveURL(/\/login(?:\?returnTo=|$)/);
  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL(/127\.0\.0\.1:5080\/passport\/login/);
  await page.locator('input[name="Email"]').fill(email);
  await page.locator('input[name="Password"]').fill(password);
  await page.getByRole("button", { name: "继续" }).click();
}
