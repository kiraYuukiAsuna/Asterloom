import { afterEach, describe, expect, it } from "vitest";

import {
  getActiveLocale,
  normalizeLocale,
  setActiveLocale,
  translate,
  translateForLocale,
} from "@/lib/i18n/locale";

describe("UI locale", () => {
  afterEach(() => setActiveLocale("en"));

  it("normalizes Chinese variants and falls back to English", () => {
    expect(normalizeLocale("zh")).toBe("zh-CN");
    expect(normalizeLocale("zh-Hans-CN")).toBe("zh-CN");
    expect(normalizeLocale("en-US")).toBe("en");
    expect(normalizeLocale("fr-FR")).toBe("en");
    expect(normalizeLocale(null)).toBe("en");
  });

  it("uses English source messages as the stable fallback", () => {
    expect(translateForLocale("en", "  Create   tenant  ")).toBe(
      "Create tenant",
    );
    expect(translateForLocale("zh-CN", "unknown technical-value")).toBe(
      "unknown technical-value",
    );
  });

  it("translates exact and parameterized messages", () => {
    expect(translateForLocale("zh-CN", "Feature flags")).toBe("功能开关");
    expect(
      translateForLocale("zh-CN", "View audit event event-42"),
    ).toBe("查看审计事件 event-42");
    expect(
      translateForLocale("zh-CN", "Remove operator from tenant-a?"),
    ).toBe("确定从 tenant-a 移除 operator 吗？");
  });

  it("supports named replacement values", () => {
    expect(
      translateForLocale("zh-CN", "Count: {count}", { count: 3 }),
    ).toBe("Count: 3");
  });

  it("uses the active locale for client-side notifications", () => {
    setActiveLocale("zh-CN");
    expect(getActiveLocale()).toBe("zh-CN");
    expect(translate("Unable to sign out. Please try again.")).toBe(
      "无法退出登录，请重试。",
    );
  });
});
