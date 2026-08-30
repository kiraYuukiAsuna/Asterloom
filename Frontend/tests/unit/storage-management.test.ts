import { describe, expect, it } from "vitest";

import {
  formatStorageMetadata,
  parseStorageMetadata,
} from "@/lib/api/storage-management";

describe("storage metadata editor", () => {
  it("normalizes, sorts, and round-trips metadata", () => {
    const parsed = parseStorageMetadata("Source=console\nretention=standard");

    expect(parsed).toEqual({ source: "console", retention: "standard" });
    expect(formatStorageMetadata(parsed)).toBe(
      "retention=standard\nsource=console",
    );
  });

  it("preserves equals signs in values and rejects malformed lines", () => {
    expect(parseStorageMetadata("signature=sha256=abc")).toEqual({
      signature: "sha256=abc",
    });
    expect(() => parseStorageMetadata("missing-separator")).toThrow(/key=value/);
    expect(() => parseStorageMetadata("INVALID KEY=value")).toThrow();
  });
});
