import { expect, test } from "@playwright/test";

test("shows the platform API response through the BFF", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/login$/);
  await expect(
    page.locator('[data-ui-action="start-passport-login"]'),
  ).toBeVisible();

  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL(/127\.0\.0\.1:5080\/passport\/login/);
  await page.locator('input[name="Email"]').fill("admin@asterloom.test");
  await page
    .locator('input[name="Password"]')
    .fill("Asterloom-E2E-Admin!2026");
  await page.getByRole("button", { name: "继续" }).click();

  await expect(page).toHaveURL("http://localhost:3000/");
  const session = await page.request.get("/api/auth/session");
  expect(session.ok()).toBeTruthy();
  expect((await session.json()).actor.email).toBe("admin@asterloom.test");
  const cookies = await page.context().cookies();
  const sessionCookie = cookies.find((cookie) =>
    cookie.name.includes("asterloom_session"),
  );
  expect(sessionCookie?.httpOnly).toBeTruthy();
  expect(sessionCookie?.value.split(".")).toHaveLength(1);

  await expect(
    page.getByRole("heading", { name: /one foundation/i }),
  ).toBeVisible();
  await expect(
    page.locator('[data-ui-action="view-platform-info"]'),
  ).toBeVisible();
  await expect(page.getByText("Operational", { exact: true })).toBeVisible();
  await expect(page.getByTestId("capability-rpc")).toContainText("Ready");
  await expect(page.getByTestId("capability-identity")).toContainText("Ready");

  const rejectedMutation = await page.evaluate(async () => {
    const response = await fetch("/api/asterloom/api/v1/platform/info", {
      body: "{}",
      headers: { "content-type": "application/json" },
      method: "POST",
    });
    return { body: await response.json(), status: response.status };
  });
  expect(rejectedMutation.status).toBe(403);
  expect(rejectedMutation.body.code).toBe("CSRF_REJECTED");

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL("http://localhost:3000/login?loggedOut=1");
  await expect(page.getByText("You have signed out successfully.")).toBeVisible();

  const signedOutSession = await page.request.get("/api/auth/session");
  expect(signedOutSession.status()).toBe(401);
  const signedOutCookies = await page.context().cookies();
  expect(
    signedOutCookies.some((cookie) =>
      cookie.name.includes("asterloom_session"),
    ),
  ).toBeFalsy();
});
