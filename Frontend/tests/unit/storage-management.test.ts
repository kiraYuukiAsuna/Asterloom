import { afterEach, describe, expect, it, vi } from "vitest";

import {
  formatStorageMetadata,
  parseStorageMetadata,
  uploadStorageTransfer,
} from "@/lib/api/storage-management";

afterEach(() => {
  vi.unstubAllGlobals();
});

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

  it("preserves signed headers when a normalized transfer ticket is parsed again", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await uploadStorageTransfer(
      "unused-for-direct-transfer",
      {
        expiresAt: "2026-08-31T12:00:00Z",
        method: "PUT",
        requiredHeaders: {
          "content-type": "text/plain",
          "x-amz-meta-asterloom-sha256": "abc123",
          "x-amz-meta-asterloom-size": "3",
        },
        url: "https://storage.example.test/upload",
      },
      new File(["abc"], "artifact.txt", { type: "text/plain" }),
    );

    const request = fetchMock.mock.calls[0]?.[1] as RequestInit;
    const headers = request.headers as Headers;
    expect(Object.fromEntries(headers.entries())).toEqual({
      "content-type": "text/plain",
      "x-amz-meta-asterloom-sha256": "abc123",
      "x-amz-meta-asterloom-size": "3",
    });
  });
});
