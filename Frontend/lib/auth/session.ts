import "server-only";

import { cookies } from "next/headers";

import { getAuthConfig } from "@/lib/auth/config";
import { randomOpaqueValue, seal, unseal } from "@/lib/auth/crypto";
import {
  acquireLock,
  get,
  namespacedKey,
  put,
  releaseLock,
  remove,
  take,
} from "@/lib/auth/store";
import {
  exchangeRefreshToken,
  OidcTokenError,
} from "@/lib/auth/token-client";
import {
  loginTransactionSchema,
  sessionSchema,
  type BffSession,
  type LoginTransaction,
} from "@/lib/auth/types";

const sessionLifetimeMs = 8 * 60 * 60 * 1000;
const loginTransactionTtlSeconds = 10 * 60;
const refreshBeforeExpiryMs = 30_000;

export class SessionStoreUnavailableError extends Error {
  constructor() {
    super("The BFF session store or Passport is temporarily unavailable.");
    this.name = "SessionStoreUnavailableError";
  }
}

export function createSession(
  tokens: {
    accessExpiresAt: number;
    accessToken: string;
    actor: NonNullable<BffSession["actor"]>;
    idToken?: string;
    refreshToken?: string;
  },
): { id: string; record: BffSession } {
  return {
    id: randomOpaqueValue(),
    record: {
      absoluteExpiresAt: Date.now() + sessionLifetimeMs,
      accessExpiresAt: tokens.accessExpiresAt,
      accessToken: tokens.accessToken,
      actor: tokens.actor,
      csrfToken: randomOpaqueValue(),
      idToken: tokens.idToken,
      refreshToken: tokens.refreshToken,
    },
  };
}

export async function saveSession(
  id: string,
  record: BffSession,
): Promise<void> {
  const validated = sessionSchema.parse(record);
  const ttl = Math.max(
    1,
    Math.ceil((validated.absoluteExpiresAt - Date.now()) / 1000),
  );
  await put(namespacedKey("session", id), seal(validated), ttl);
}

export async function readSession(id: string): Promise<BffSession | null> {
  const encrypted = await get(namespacedKey("session", id));
  if (!encrypted) {
    return null;
  }

  try {
    const session = sessionSchema.parse(unseal(encrypted));
    if (session.absoluteExpiresAt <= Date.now()) {
      await deleteSession(id);
      return null;
    }
    return session;
  } catch {
    await deleteSession(id);
    return null;
  }
}

export async function deleteSession(id: string): Promise<void> {
  await remove(namespacedKey("session", id));
}

export async function readCurrentSession(): Promise<{
  id: string;
  record: BffSession;
} | null> {
  const config = getAuthConfig();
  const id = (await cookies()).get(config.sessionCookieName)?.value;
  if (!id) {
    return null;
  }
  const record = await readSession(id);
  return record ? { id, record } : null;
}

export async function ensureFreshSession(
  id: string,
  record: BffSession,
  force = false,
): Promise<BffSession | null> {
  if (
    !force &&
    record.accessExpiresAt > Date.now() + refreshBeforeExpiryMs
  ) {
    return record;
  }
  if (!record.refreshToken) {
    await deleteSession(id);
    return null;
  }

  const lockKey = namespacedKey("refresh-lock", id);
  const lockToken = await acquireLock(lockKey, 10_000);
  if (!lockToken) {
    for (let attempt = 0; attempt < 20; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 100));
      const updated = await readSession(id);
      if (!updated) {
        return null;
      }
      if (
        updated.accessToken !== record.accessToken ||
        updated.accessExpiresAt > Date.now() + refreshBeforeExpiryMs
      ) {
        return updated;
      }
    }
    throw new SessionStoreUnavailableError();
  }

  try {
    const latest = (await readSession(id)) ?? record;
    if (
      !force &&
      latest.accessExpiresAt > Date.now() + refreshBeforeExpiryMs
    ) {
      return latest;
    }
    if (!latest.refreshToken) {
      await deleteSession(id);
      return null;
    }

    try {
      const tokens = await exchangeRefreshToken(latest.refreshToken);
      const updated: BffSession = {
        ...latest,
        accessExpiresAt: tokens.accessExpiresAt,
        accessToken: tokens.accessToken,
        actor: tokens.actor ?? latest.actor,
        idToken: tokens.idToken ?? latest.idToken,
        refreshToken: tokens.refreshToken ?? latest.refreshToken,
      };
      await saveSession(id, updated);
      return updated;
    } catch (error) {
      if (error instanceof OidcTokenError && error.permanent) {
        await deleteSession(id);
        return null;
      }
      throw new SessionStoreUnavailableError();
    }
  } finally {
    await releaseLock(lockKey, lockToken);
  }
}

export async function saveLoginTransaction(
  state: string,
  transaction: LoginTransaction,
): Promise<void> {
  await put(
    namespacedKey("login", state),
    seal(loginTransactionSchema.parse(transaction)),
    loginTransactionTtlSeconds,
  );
}

export async function takeLoginTransaction(
  state: string,
): Promise<LoginTransaction | null> {
  const encrypted = await take(namespacedKey("login", state));
  if (!encrypted) {
    return null;
  }
  try {
    return loginTransactionSchema.parse(unseal(encrypted));
  } catch {
    return null;
  }
}

export const sessionCookieMaxAge = sessionLifetimeMs / 1000;
export const oidcCookieMaxAge = loginTransactionTtlSeconds;
