import "server-only";

import { z } from "zod";

const urlSchema = z.string().url();

export type AuthConfig = {
  backendUrl: string;
  clientId: string;
  clientSecret?: string;
  encryptionKey?: string;
  issuer: string;
  oidcCookieName: string;
  passportPublicUrl: string;
  redisUrl?: string;
  redisPassword?: string;
  secureCookies: boolean;
  sessionCookieName: string;
  sessionStore: "memory" | "redis";
  webOrigin: string;
};

export function getAuthConfig(): AuthConfig {
  const allowInsecureDevelopment =
    process.env.ASTERLOOM_ALLOW_INSECURE_DEVELOPMENT === "true";
  const production =
    process.env.NODE_ENV === "production" && !allowInsecureDevelopment;
  const backendUrl = normalizeUrl(
    process.env.ASTERLOOM_BACKEND_URL ?? "http://127.0.0.1:5080",
    "ASTERLOOM_BACKEND_URL",
  );
  const passportPublicUrl = normalizeUrl(
    process.env.ASTERLOOM_PASSPORT_PUBLIC_URL ?? backendUrl,
    "ASTERLOOM_PASSPORT_PUBLIC_URL",
  );
  const webOrigin = normalizeOrigin(
    process.env.ASTERLOOM_WEB_ORIGIN ?? "http://localhost:3000",
    "ASTERLOOM_WEB_ORIGIN",
  );
  const issuer = normalizeUrl(
    process.env.ASTERLOOM_OIDC_ISSUER ?? passportPublicUrl,
    "ASTERLOOM_OIDC_ISSUER",
  );
  const clientId =
    process.env.ASTERLOOM_OIDC_CLIENT_ID?.trim() || "asterloom-web";
  const clientSecret = normalizeOptional(
    process.env.ASTERLOOM_OIDC_CLIENT_SECRET,
  );
  const redisUrl = normalizeOptional(
    process.env.ASTERLOOM_SESSION_REDIS_URL,
  );
  const redisPassword = normalizeOptional(
    process.env.ASTERLOOM_SESSION_REDIS_PASSWORD,
  );
  const sessionStoreValue =
    process.env.ASTERLOOM_SESSION_STORE ?? (redisUrl ? "redis" : "memory");
  const sessionStore = z
    .enum(["memory", "redis"])
    .parse(sessionStoreValue) as "memory" | "redis";
  const encryptionKey = normalizeOptional(
    process.env.ASTERLOOM_SESSION_ENCRYPTION_KEY,
  );

  if (sessionStore === "redis" && !redisUrl) {
    throw new Error(
      "ASTERLOOM_SESSION_REDIS_URL is required for the Redis session store.",
    );
  }

  if (production) {
    for (const [name, value] of [
      ["ASTERLOOM_PASSPORT_PUBLIC_URL", passportPublicUrl],
      ["ASTERLOOM_WEB_ORIGIN", webOrigin],
      ["ASTERLOOM_OIDC_ISSUER", issuer],
    ] as const) {
      if (new URL(value).protocol !== "https:") {
        throw new Error(name + " must use HTTPS in production.");
      }
    }

    if (sessionStore !== "redis") {
      throw new Error("Production BFF sessions require the Redis store.");
    }
    if (!redisPassword) {
      throw new Error(
        "ASTERLOOM_SESSION_REDIS_PASSWORD is required in production.",
      );
    }
    if (!clientSecret) {
      throw new Error(
        "ASTERLOOM_OIDC_CLIENT_SECRET is required in production.",
      );
    }
    if (!encryptionKey) {
      throw new Error(
        "ASTERLOOM_SESSION_ENCRYPTION_KEY is required in production.",
      );
    }
  }

  const secureCookies = new URL(webOrigin).protocol === "https:";
  return {
    backendUrl,
    clientId,
    clientSecret,
    encryptionKey,
    issuer,
    oidcCookieName: secureCookies
      ? "__Host-asterloom_oidc"
      : "asterloom_oidc_development",
    passportPublicUrl,
    redisPassword,
    redisUrl,
    secureCookies,
    sessionCookieName: secureCookies
      ? "__Host-asterloom_session"
      : "asterloom_session_development",
    sessionStore,
    webOrigin,
  };
}

export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//")) {
    return "/";
  }

  const parsed = new URL(value, "https://asterloom.invalid");
  return parsed.origin === "https://asterloom.invalid"
    ? parsed.pathname + parsed.search + parsed.hash
    : "/";
}

function normalizeUrl(value: string, name: string): string {
  const validated = urlSchema.safeParse(value);
  if (!validated.success) {
    throw new Error(name + " is invalid.");
  }
  const parsed = new URL(validated.data);
  parsed.hash = "";
  parsed.search = "";
  return parsed.toString().replace(/\/+$/, "");
}

function normalizeOrigin(value: string, name: string): string {
  const validated = urlSchema.safeParse(value);
  if (!validated.success) {
    throw new Error(name + " is invalid.");
  }
  const parsed = new URL(validated.data);
  if (parsed.pathname !== "/" || parsed.search || parsed.hash) {
    throw new Error(name + " must be an origin without a path, query, or hash.");
  }
  return parsed.origin;
}

function normalizeOptional(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}
