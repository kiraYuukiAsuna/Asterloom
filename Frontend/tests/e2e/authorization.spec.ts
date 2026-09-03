import { expect, test } from "@playwright/test";

import { signIn, webOrigin, webUrl } from "./support/environment";

test("manages the complete authorization surface through the Web Console", async ({
  page,
}) => {
  test.setTimeout(90_000);
  page.setDefaultTimeout(10_000);
  page.on("dialog", (dialog) => dialog.accept());

  await signIn(page, "/authorization/roles");
  await expect(page).toHaveURL(webUrl("/authorization/roles"));
  await expect(page.locator("[data-authorization-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  const sessionResponse = await page.request.get("/api/auth/session");
  expect(sessionResponse.ok()).toBeTruthy();
  const session = (await sessionResponse.json()) as { csrfToken: string };
  const mutationHeaders = {
    origin: webOrigin,
    "x-csrf-token": session.csrfToken,
  };

  const suffix = Date.now().toString(36).slice(-8);
  const tenantResponse = await page.request.post("/api/asterloom/api/v1/tenants", {
    data: { displayName: "Authorization E2E Tenant", slug: `authorization-${suffix}` },
    headers: mutationHeaders,
  });
  expect(tenantResponse.ok()).toBeTruthy();
  const tenant = (await tenantResponse.json()) as { id: string };
  const applicationResponse = await page.request.post(
    `/api/asterloom/api/v1/tenants/${tenant.id}/applications`,
    {
      data: {
        displayName: "Authorization E2E Application",
        slug: `authorization-${suffix}`,
      },
      headers: mutationHeaders,
    },
  );
  expect(applicationResponse.ok()).toBeTruthy();
  const application = (await applicationResponse.json()) as { id: string };
  const environmentResponse = await page.request.post(
    `/api/asterloom/api/v1/tenants/${tenant.id}/applications/${application.id}/environments`,
    {
      data: {
        displayName: "Authorization E2E Environment",
        environmentType: "ENVIRONMENT_TYPE_DEVELOPMENT",
        isProtected: false,
        slug: `authorization-${suffix}`,
      },
      headers: mutationHeaders,
    },
  );
  expect(environmentResponse.ok()).toBeTruthy();
  await page.reload();
  await expect(page.locator("[data-authorization-workspace]")).toHaveAttribute(
    "data-hydrated",
    "true",
  );

  const tenantLabel = `Authorization E2E Tenant (authorization-${suffix})`;
  const applicationLabel = `Authorization E2E Application (authorization-${suffix})`;
  const environmentLabel = `Authorization E2E Environment (authorization-${suffix})`;
  await page.getByLabel("Tenant", { exact: true }).fill(tenantLabel);
  await page.getByLabel("Application", { exact: true }).fill(applicationLabel);

  await expect(page.locator('[data-ui-action="list-roles"]')).toBeVisible();
  await expect(page.locator('[data-ui-action="list-permissions"]')).toBeVisible();
  await expect(
    page
      .locator('[data-ui-action="list-permissions"]')
      .getByText("platform.environment.read", { exact: true }),
  ).toBeVisible();

  const roleKey = `auth-e2e-${suffix}`;
  const actorId = `authorization-e2e-actor-${suffix}`;
  const policyName = `E2E release rule ${suffix}`;
  const updatedPolicyName = `E2E release deny rule ${suffix}`;
  const permissionKey = `orders.refund-${suffix}`;

  await page.getByTestId("authorization-tab-permissions").click();
  await page.getByLabel("Include archived").check();
  const createPermissionCard = page.locator('[data-ui-action="create-permission"]');
  await createPermissionCard
    .locator('input[name="applicationPermissionKey"]')
    .fill(permissionKey);
  await createPermissionCard
    .locator('input[name="applicationPermissionDisplayName"]')
    .fill("Refund E2E orders");
  await createPermissionCard
    .locator('textarea[name="applicationPermissionDescription"]')
    .fill("Application permission exercised by the browser contract.");
  await createPermissionCard.getByRole("button", { name: "Create permission" }).click();

  const permissionRow = page.getByTestId(`authorization-permission-${permissionKey}`);
  await expect(permissionRow).toContainText("Refund E2E orders");
  await permissionRow.getByRole("button", { name: "Edit" }).click();
  const updatePermissionForm = permissionRow.locator(
    '[data-ui-action="update-permission"]',
  );
  await updatePermissionForm.getByLabel("Display name").fill("Refund E2E orders updated");
  await updatePermissionForm.getByRole("button", { name: "Save permission" }).click();
  await expect(permissionRow).toContainText("Refund E2E orders updated");
  await permissionRow.locator('[data-ui-action="archive-permission"]').click();
  await expect(permissionRow.locator('[data-ui-action="restore-permission"]')).toBeVisible();
  await permissionRow.locator('[data-ui-action="restore-permission"]').click();
  await expect(permissionRow.locator('[data-ui-action="archive-permission"]')).toBeVisible();

  await page.getByTestId("authorization-tab-roles").click();

  await page.getByLabel("Include archived").check();
  const createRoleCard = page.locator('[data-ui-action="create-role"]');
  await createRoleCard.locator('input[name="roleKey"]').fill(roleKey);
  await createRoleCard
    .locator('input[name="roleDisplayName"]')
    .fill("E2E Release Operator");
  await createRoleCard
    .locator('textarea[name="roleDescription"]')
    .fill("Role exercised by the browser contract.");
  await createRoleCard
    .locator('textarea[name="rolePermissions"]')
    .fill(`${permissionKey}, platform.environment.read, platform.environment.update`);
  await createRoleCard.getByRole("button", { name: "Create role" }).click();

  const roleRow = page.getByTestId(`authorization-role-${roleKey}`);
  await expect(roleRow).toContainText("E2E Release Operator");
  await roleRow.getByRole("button", { name: "Edit" }).click();
  const updateRoleForm = roleRow.locator('[data-ui-action="update-role"]');
  await updateRoleForm
    .locator('input[name="editRoleDisplayName"]')
    .fill("E2E Release Operator Updated");
  await updateRoleForm.getByRole("button", { name: "Save role" }).click();
  await expect(roleRow).toContainText("E2E Release Operator Updated");
  await roleRow.locator('[data-ui-action="archive-role"]').click();
  await expect(roleRow.locator('[data-ui-action="restore-role"]')).toBeVisible();
  await roleRow.locator('[data-ui-action="restore-role"]').click();
  await expect(roleRow.locator('[data-ui-action="archive-role"]')).toBeVisible();

  await page.getByTestId("authorization-tab-bindings").click();
  const bindingsCard = page.locator('[data-ui-action="list-role-bindings"]');
  await expect(bindingsCard).toBeVisible();
  await bindingsCard.getByLabel("Include removed bindings").check();
  const bindingForm = page.locator('[data-ui-action="set-role-binding"]');
  await bindingForm.locator('input[name="bindingActorId"]').fill(actorId);
  await bindingForm
    .getByLabel("Role", { exact: true })
    .fill(`E2E Release Operator Updated (${roleKey})`);
  await bindingForm.getByLabel("Tenant", { exact: true }).fill(tenantLabel);
  await bindingForm.getByLabel("Application", { exact: true }).fill(applicationLabel);
  const [createBindingResponse] = await Promise.all([
    page.waitForResponse(
      (response) =>
        response.url().includes("/api/asterloom/api/v1/authorization/role-bindings/") &&
        response.request().method() === "PUT",
    ),
    bindingForm.getByRole("button", { name: "Create binding" }).click(),
  ]);
  expect(createBindingResponse.ok(), await createBindingResponse.text()).toBeTruthy();

  const bindingRow = page
    .locator('[data-testid^="authorization-binding-"]')
    .filter({ hasText: actorId });
  await expect(bindingRow).toContainText(roleKey);
  await bindingRow.locator('[data-ui-action="remove-role-binding"]').click();
  await expect(bindingRow).toContainText("Archived");
  await bindingRow.getByRole("button", { name: "Reactivate" }).click();
  await bindingForm.getByRole("button", { name: "Save binding" }).click();
  await expect(bindingRow).toContainText("Active");

  await page.getByTestId("authorization-tab-policies").click();
  const policiesCard = page.locator('[data-ui-action="list-policy-rules"]');
  await expect(policiesCard).toBeVisible();
  await policiesCard.getByLabel("Include archived policies").check();
  const createPolicyCard = page.locator('[data-ui-action="create-policy-rule"]');
  await createPolicyCard.locator('input[name="policyName"]').fill(policyName);
  await createPolicyCard.locator('input[name="policySubject"]').fill(actorId);
  await createPolicyCard
    .locator('input[name="policyPermission"]')
    .fill("platform.environment.update");
  await createPolicyCard.locator('input[name="policyResourceType"]').fill("order");
  await createPolicyCard.locator('input[name="policyResourceId"]').fill("order-42");
  await createPolicyCard.getByLabel("Enable ABAC attribute condition").check();
  await createPolicyCard
    .locator('input[name="authorizationPolicyConditionConditionAttribute"]')
    .fill("subject.department");
  await createPolicyCard
    .locator('input[name="authorizationPolicyConditionConditionValue"]')
    .fill("finance");
  await createPolicyCard.getByLabel("Tenant", { exact: true }).fill(tenantLabel);
  await createPolicyCard.getByLabel("Application", { exact: true }).fill(applicationLabel);
  await createPolicyCard.getByRole("button", { name: "Create policy" }).click();

  const policyRow = page
    .locator('[data-testid^="authorization-policy-"]')
    .filter({ hasText: policyName });
  await expect(policyRow).toContainText("Allow");
  await policyRow.locator('[data-ui-action="update-policy-rule"]').click();
  await createPolicyCard
    .locator('input[name="policyName"]')
    .fill(updatedPolicyName);
  await createPolicyCard
    .locator('select[name="policyEffect"]')
    .selectOption("POLICY_EFFECT_DENY");
  await createPolicyCard.getByRole("button", { name: "Save policy" }).click();
  const updatedPolicyRow = page
    .locator('[data-testid^="authorization-policy-"]')
    .filter({ hasText: updatedPolicyName });
  await expect(updatedPolicyRow).toContainText("Deny");
  await updatedPolicyRow.locator('[data-ui-action="archive-policy-rule"]').click();
  await expect(
    updatedPolicyRow.locator('[data-ui-action="restore-policy-rule"]'),
  ).toBeVisible();
  await updatedPolicyRow.locator('[data-ui-action="restore-policy-rule"]').click();
  await expect(
    updatedPolicyRow.locator('[data-ui-action="archive-policy-rule"]'),
  ).toBeVisible();

  await page.getByTestId("authorization-tab-simulator").click();
  const revisionsCard = page.locator('[data-ui-action="list-policy-revisions"]');
  await expect(revisionsCard).toBeVisible();
  await expect(revisionsCard.getByText(/Revision \d+/).first()).toBeVisible();

  const simulator = page.locator('[data-ui-action="simulate-authorization"]');
  await simulator.locator('input[name="simulationActorId"]').fill(actorId);
  await simulator
    .locator('input[name="simulationPermission"]')
    .fill("platform.environment.update");
  await simulator.getByLabel("Tenant", { exact: true }).fill(tenantLabel);
  await simulator.getByLabel("Application", { exact: true }).fill(applicationLabel);
  await simulator.getByLabel("Environment", { exact: true }).fill(environmentLabel);
  await simulator.locator('input[name="simulationResourceType"]').fill("order");
  await simulator.locator('input[name="simulationResourceId"]').fill("order-42");
  await simulator.getByRole("button", { name: "Simulate", exact: true }).click();
  await expect(simulator.getByText("Denied", { exact: true })).toBeVisible();

  await simulator.locator('[data-ui-action="check-permission"]').click();
  await expect(simulator.getByText("Allowed", { exact: true })).toBeVisible();
});
