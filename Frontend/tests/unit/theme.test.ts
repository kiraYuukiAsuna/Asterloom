import { describe, expect, it } from "vitest";

import {
  nextThemePreference,
  normalizeThemePreference,
  resolveTheme,
} from "@/lib/ui/theme";

describe("theme preference", () => {
  it("falls back to the system preference for missing or invalid values", () => {
    expect(normalizeThemePreference(null)).toBe("system");
    expect(normalizeThemePreference("sepia")).toBe("system");
  });

  it("keeps supported preferences", () => {
    expect(normalizeThemePreference("system")).toBe("system");
    expect(normalizeThemePreference("light")).toBe("light");
    expect(normalizeThemePreference("dark")).toBe("dark");
  });

  it("resolves system mode while preserving explicit modes", () => {
    expect(resolveTheme("system", true)).toBe("dark");
    expect(resolveTheme("system", false)).toBe("light");
    expect(resolveTheme("light", true)).toBe("light");
    expect(resolveTheme("dark", false)).toBe("dark");
  });

  it("cycles through system, light, and dark", () => {
    expect(nextThemePreference("system")).toBe("light");
    expect(nextThemePreference("light")).toBe("dark");
    expect(nextThemePreference("dark")).toBe("system");
  });
});
