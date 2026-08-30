import { expect, test } from "@playwright/test";

import { signIn } from "./support/environment";

test("switches and persists the locale on the sign-in experience", async ({
  page,
}) => {
  await page.addInitScript(() => {
    if (!window.localStorage.getItem("asterloom-locale")) {
      window.localStorage.setItem("asterloom-locale", "en");
    }
  });
  await page.goto("/login");

  const root = page.locator("html");
  const toggle = page.getByTestId("locale-toggle");
  await expect(root).toHaveAttribute("lang", "en");
  await expect(page.getByRole("heading", { name: "Continue with Passport" })).toBeVisible();

  await toggle.click();
  await expect(root).toHaveAttribute("lang", "zh-CN");
  await expect(page.getByRole("heading", { name: "使用 Passport 继续" })).toBeVisible();

  await page.reload();
  await expect(root).toHaveAttribute("lang", "zh-CN");
  await expect(page.getByText("一个安全会话。")).toBeVisible();

  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL((url) => url.pathname === "/passport/login");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
  await expect(page.getByRole("heading", { name: "账户登录" })).toBeVisible();

  await page.goto("/login");
  await page.getByTestId("locale-toggle").click();
  await page.locator('[data-ui-action="start-passport-login"]').click();
  await expect(page).toHaveURL((url) => url.pathname === "/passport/login");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect(page.getByRole("heading", { name: "Account sign in" })).toBeVisible();
});

test("renders every management capability in Chinese and switches back to English", async ({
  page,
}) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(25_000);
  await page.addInitScript(() => {
    window.localStorage.setItem("asterloom-locale", "zh-CN");
  });

  await signIn(page, "/");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
  await expect(page.getByRole("navigation", { name: "主导航" })).toBeVisible();

  const capabilities = [
    { path: "/", copy: "运行正常" },
    { path: "/tenants", copy: "平台资源工作区" },
    { path: "/identity/users", copy: "身份管理中心" },
    { path: "/authorization/roles", copy: "权限控制中心" },
    { path: "/audit", copy: "审计记录" },
    { path: "/features", copy: "功能交付控制" },
    { path: "/targeting/segments", copy: "定向分群" },
    { path: "/config", copy: "动态配置" },
    { path: "/releases", copy: "版本发布中心" },
    { path: "/analytics/explorer", copy: "产品分析中心" },
    { path: "/telemetry/health", copy: "遥测控制中心" },
    { path: "/operations/apis", copy: "API 与健康运维" },
    { path: "/storage/buckets", copy: "文件存储中心" },
  ] as const;

  for (const capability of capabilities) {
    await page.goto(capability.path);
    await expect(page.getByText(capability.copy, { exact: true }).first()).toBeVisible();
  }

  await page.getByTestId("locale-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect(page.getByText("Storage control center", { exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Primary navigation" })).toBeVisible();
});
