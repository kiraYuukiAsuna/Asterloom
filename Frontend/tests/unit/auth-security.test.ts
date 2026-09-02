import { afterEach, describe, expect, it, vi } from "vitest";

import { getAuthConfig, safeReturnTo } from "@/lib/auth/config";
import { sessionCookieOptions } from "@/lib/auth/cookies";
import {
  randomOpaqueValue,
  safeEqual,
  seal,
  unseal,
} from "@/lib/auth/crypto";
import { isMutation } from "@/lib/auth/request-security";
import {
  createSession,
  persistentSessionCookieMaxAge,
} from "@/lib/auth/session";

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
});

describe("BFF authentication security", () => {
  it.each([
    [undefined, "/"],
    [null, "/"],
    ["https://attacker.example/path", "/"],
    ["//attacker.example/path", "/"],
    ["/\\attacker.example/path", "/"],
    ["/applications?status=active#result", "/applications?status=active#result"],
  ])("normalizes a return target without permitting an open redirect", (value, expected) => {
    expect(safeReturnTo(value)).toBe(expected);
  });

  it("uses opaque, high-entropy browser identifiers", () => {
    const first = randomOpaqueValue();
    const second = randomOpaqueValue();

    expect(first).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(second).not.toBe(first);
    expect(first).not.toContain(".");
  });

  it("uses a browser session by default and a 30-day session when remembered", () => {
    const now = Date.UTC(2026, 8, 3, 0, 0, 0);
    vi.spyOn(Date, "now").mockReturnValue(now);
    const tokens = {
      accessExpiresAt: now + 10 * 60 * 1000,
      accessToken: "access-token",
      actor: {
        name: "Asterloom Admin",
        roles: ["SuperAdministrator"],
        subject: "admin-user",
      },
      refreshToken: "refresh-token",
    };

    const browserSession = createSession(tokens);
    const persistentSession = createSession(tokens, true);

    expect(browserSession.record.absoluteExpiresAt).toBe(
      now + 8 * 60 * 60 * 1000,
    );
    expect(persistentSession.record.absoluteExpiresAt).toBe(
      now + 30 * 24 * 60 * 60 * 1000,
    );
    expect(sessionCookieOptions(false).maxAge).toBeUndefined();
    expect(sessionCookieOptions(true).maxAge).toBe(
      persistentSessionCookieMaxAge,
    );
    expect(persistentSessionCookieMaxAge).toBe(30 * 24 * 60 * 60);
  });

  it("authenticates encrypted server-side session envelopes", () => {
    vi.stubEnv(
      "ASTERLOOM_SESSION_ENCRYPTION_KEY",
      Buffer.alloc(32, 7).toString("base64"),
    );
    const payload = { accessToken: "not-for-the-browser", version: 1 };
    const envelope = seal(payload);

    expect(unseal(envelope)).toEqual(payload);

    const parts = envelope.split(".");
    const ciphertext = Buffer.from(parts[3], "base64url");
    ciphertext[0] ^= 1;
    parts[3] = ciphertext.toString("base64url");
    expect(() => unseal(parts.join("."))).toThrow();
  });

  it("rejects invalid session keys and compares CSRF values safely", () => {
    vi.stubEnv(
      "ASTERLOOM_SESSION_ENCRYPTION_KEY",
      Buffer.alloc(31).toString("base64"),
    );

    expect(() => seal({})).toThrow(/exactly 32 bytes/);
    expect(safeEqual("same-token", "same-token")).toBe(true);
    expect(safeEqual("same-token", "other-token")).toBe(false);
    expect(safeEqual("short", "longer")).toBe(false);
  });

  it("fails closed when production endpoints are not HTTPS", () => {
    vi.stubEnv("NODE_ENV", "production");
    vi.stubEnv("ASTERLOOM_ALLOW_INSECURE_DEVELOPMENT", "false");
    vi.stubEnv("ASTERLOOM_PASSPORT_PUBLIC_URL", "http://passport.example.test");
    vi.stubEnv("ASTERLOOM_WEB_ORIGIN", "https://console.example.test");
    vi.stubEnv("ASTERLOOM_OIDC_ISSUER", "https://passport.example.test");

    expect(() => getAuthConfig()).toThrow(
      /ASTERLOOM_PASSPORT_PUBLIC_URL must use HTTPS/,
    );
  });

  it("classifies only safe methods as non-mutating", () => {
    expect(isMutation("GET")).toBe(false);
    expect(isMutation("head")).toBe(false);
    expect(isMutation("OPTIONS")).toBe(false);
    expect(isMutation("POST")).toBe(true);
    expect(isMutation("PATCH")).toBe(true);
    expect(isMutation("DELETE")).toBe(true);
  });
});
