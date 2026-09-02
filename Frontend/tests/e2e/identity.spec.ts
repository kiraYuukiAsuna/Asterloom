import { expect, test, type Browser } from "@playwright/test";

import { signIn, webOrigin, webUrl } from "./support/environment";

test("manages the complete Identity surface through the Web Console", async ({ browser, page }) => {
  test.setTimeout(120_000);
  page.setDefaultTimeout(12_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/identity/users");
  await expect(page).toHaveURL(webUrl("/identity/users"));
  await expect(page.locator("[data-identity-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  const suffix = Date.now().toString(36).slice(-8);
  const email = `identity-e2e-${suffix}@asterloom.test`;
  const directEmail = `identity-direct-${suffix}@asterloom.test`;
  const clientId = `identity-e2e-${suffix}`;
  const scopeName = `identity.e2e.${suffix}`;
  const sessionResponse = await page.request.get("/api/auth/session");
  expect(sessionResponse.ok()).toBeTruthy();
  const session = (await sessionResponse.json()) as { csrfToken: string };
  const mutationHeaders = {
    origin: webOrigin,
    "x-csrf-token": session.csrfToken,
  };
  const tenantResponse = await page.request.post("/api/asterloom/api/v1/tenants", {
    data: { displayName: "Identity E2E Tenant", slug: `identity-${suffix}` },
    headers: mutationHeaders,
  });
  expect(tenantResponse.ok()).toBeTruthy();
  const tenant = (await tenantResponse.json()) as { id: string };
  const applicationResponse = await page.request.post(
    `/api/asterloom/api/v1/tenants/${tenant.id}/applications`,
    {
      data: { displayName: "Identity E2E Application", slug: `identity-${suffix}` },
      headers: mutationHeaders,
    },
  );
  expect(applicationResponse.ok()).toBeTruthy();
  const application = (await applicationResponse.json()) as { id: string };

  const createAccount = page.locator('[data-ui-action="create-user"]');
  await createAccount.getByLabel("Email", { exact: true }).fill(directEmail);
  await createAccount.getByLabel("Display name").fill("Direct Identity User");
  await createAccount.getByLabel("Initial password").fill("Direct-Identity-Password!2026");
  await createAccount.getByRole("button", { name: "Create account" }).click();
  const directUserRow = page
    .locator('[data-ui-action="get-user"]')
    .filter({ hasText: directEmail });
  await directUserRow.click();
  const directEditor = page.locator('[data-ui-action="update-user"]');
  await expect(directEditor).toContainText("Email confirmed");
  const resetPassword = directEditor.locator('[data-ui-action="reset-user-password"]');
  await resetPassword.getByLabel("New password").fill("Direct-Identity-Reset!2026");
  await resetPassword.getByRole("button", { name: "Reset password" }).click();

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
  await invitationPage
    .getByRole("button", { name: /激活账户|Activate account/ })
    .click();
  await expect(
    invitationPage.getByText(/账户已激活|Account activated/, { exact: true }),
  ).toBeVisible();
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

  const usersResponse = await page.request.get(
    `/api/asterloom/api/v1/identity/users?query=${encodeURIComponent(email)}`,
  );
  expect(usersResponse.ok()).toBeTruthy();
  const userId = ((await usersResponse.json()) as { users: Array<{ id: string }> })
    .users[0]?.id;
  expect(userId).toBeTruthy();

  await page.getByTestId("identity-tab-memberships").click();
  const setMembership = page.locator('[data-ui-action="set-application-membership"]');
  await setMembership.getByLabel("User UUID").fill(userId!);
  await setMembership.getByLabel("Tenant UUID").fill(tenant.id);
  await setMembership.getByLabel("Application UUID").fill(application.id);
  await setMembership.getByRole("button", { name: "Save membership" }).click();
  const membershipList = page.locator('[data-ui-action="list-application-memberships"]');
  await membershipList.getByLabel("Include removed memberships").check();
  const membershipRow = page.getByTestId(
    `application-membership-${application.id}-${userId}`,
  );
  await expect(membershipRow).toContainText("Active");
  await membershipRow.locator('[data-ui-action="remove-application-membership"]').click();
  await expect(membershipRow).toContainText("Removed");
  await membershipRow.getByRole("button", { name: "Restore" }).click();
  await expect(membershipRow).toContainText("Active");

  await page.getByTestId("identity-tab-clients").click();
  const systemClientRow = page
    .locator('[data-ui-action="get-client"]')
    .filter({ hasText: "asterloom-web-e2e" });
  await systemClientRow.click();
  const systemClientEditor = page.locator('[data-ui-action="update-client"]');
  await expect(systemClientEditor).toContainText("System resource");
  await expect(systemClientEditor.getByTestId("identity-system-resource-notice")).toBeVisible();
  await expect(systemClientEditor.getByLabel("Display name")).toBeDisabled();
  await expect(systemClientEditor.locator('[data-ui-action="rotate-client-secret"]')).toHaveCount(0);
  await expect(systemClientEditor.locator('[data-ui-action="delete-client"]')).toHaveCount(0);
  await expect(systemClientEditor.getByRole("button", { name: "Save client" })).toHaveCount(0);

  const createClient = page.locator('[data-ui-action="create-client"]');
  await createClient.getByLabel("Client ID").fill(clientId);
  await createClient.getByLabel("Display name").fill("Identity E2E Client");
  await createClient.getByLabel("Client type").selectOption("OIDC_CLIENT_TYPE_CONFIDENTIAL");
  await createClient.getByLabel("Authorization code + PKCE").uncheck();
  await createClient.getByLabel("Refresh token").uncheck();
  await createClient.getByLabel("Client credentials").check();
  await expect(createClient.getByLabel("Trusted backend password")).toHaveCount(0);
  await createClient.getByLabel("Tenant UUID (optional)").fill(tenant.id);
  await createClient.getByLabel("Application UUID (optional)").fill(application.id);
  await createClient.getByLabel("Allow trusted backend registration").check();
  await createClient.getByLabel("Auto-join existing accounts on login").check();
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
  const systemScopeRow = page
    .locator('[data-ui-action="get-scope"]')
    .filter({ hasText: "asterloom.api" });
  await systemScopeRow.click();
  const systemScopeEditor = page.locator('[data-ui-action="update-scope"]');
  await expect(systemScopeEditor).toContainText("System resource");
  await expect(systemScopeEditor.getByTestId("identity-system-resource-notice")).toBeVisible();
  await expect(systemScopeEditor.getByLabel("Display name")).toBeDisabled();
  await expect(systemScopeEditor.locator('[data-ui-action="delete-scope"]')).toHaveCount(0);
  await expect(systemScopeEditor.getByRole("button", { name: "Save scope" })).toHaveCount(0);

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
    await expect(page).toHaveURL(webUrl("/"));
    const session = await page.request.get("/api/auth/session");
    expect(session.ok()).toBeTruthy();
    expect((await session.json()).actor.email).toBe(email);
  } finally {
    await context.close();
  }
}
