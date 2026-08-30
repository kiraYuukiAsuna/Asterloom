import "server-only";

import {
  createCipheriv,
  createDecipheriv,
  randomBytes,
  timingSafeEqual,
} from "node:crypto";

import { getAuthConfig } from "@/lib/auth/config";

const developmentKey = randomBytes(32);

export function randomOpaqueValue(bytes = 32): string {
  return randomBytes(bytes).toString("base64url");
}

export function seal(value: unknown): string {
  const iv = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", encryptionKey(), iv);
  const ciphertext = Buffer.concat([
    cipher.update(JSON.stringify(value), "utf8"),
    cipher.final(),
  ]);
  const tag = cipher.getAuthTag();
  return [
    "v1",
    iv.toString("base64url"),
    tag.toString("base64url"),
    ciphertext.toString("base64url"),
  ].join(".");
}

export function unseal(value: string): unknown {
  const parts = value.split(".");
  if (parts.length !== 4 || parts[0] !== "v1") {
    throw new Error("Unsupported encrypted session envelope.");
  }

  const decipher = createDecipheriv(
    "aes-256-gcm",
    encryptionKey(),
    Buffer.from(parts[1], "base64url"),
  );
  decipher.setAuthTag(Buffer.from(parts[2], "base64url"));
  const plaintext = Buffer.concat([
    decipher.update(Buffer.from(parts[3], "base64url")),
    decipher.final(),
  ]);
  return JSON.parse(plaintext.toString("utf8")) as unknown;
}

export function safeEqual(left: string, right: string): boolean {
  const leftBytes = Buffer.from(left);
  const rightBytes = Buffer.from(right);
  return (
    leftBytes.length === rightBytes.length &&
    timingSafeEqual(leftBytes, rightBytes)
  );
}

function encryptionKey(): Buffer {
  const configured = getAuthConfig().encryptionKey;
  if (!configured) {
    return developmentKey;
  }

  const key = Buffer.from(configured, "base64");
  if (key.length !== 32) {
    throw new Error(
      "ASTERLOOM_SESSION_ENCRYPTION_KEY must decode to exactly 32 bytes.",
    );
  }
  return key;
}
