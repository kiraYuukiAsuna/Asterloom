import "server-only";

import { createHash } from "node:crypto";
import { createClient } from "redis";

import { getAuthConfig } from "@/lib/auth/config";
import { randomOpaqueValue } from "@/lib/auth/crypto";

type MemoryValue = { expiresAt: number; value: string };
type MemoryState = {
  locks: Map<string, { expiresAt: number; token: string }>;
  values: Map<string, MemoryValue>;
};

function createRedisClient(url: string, password?: string) {
  return createClient({ password, url });
}

type AsterloomRedisClient = ReturnType<typeof createRedisClient>;

const globalState = globalThis as typeof globalThis & {
  asterloomMemoryAuthStore?: MemoryState;
  asterloomRedisClient?: AsterloomRedisClient;
  asterloomRedisConnection?: Promise<AsterloomRedisClient>;
};

function memoryState(): MemoryState {
  globalState.asterloomMemoryAuthStore ??= {
    locks: new Map(),
    values: new Map(),
  };
  return globalState.asterloomMemoryAuthStore;
}

export async function put(
  key: string,
  value: string,
  ttlSeconds: number,
): Promise<void> {
  if (getAuthConfig().sessionStore === "memory") {
    memoryState().values.set(key, {
      expiresAt: Date.now() + ttlSeconds * 1000,
      value,
    });
    return;
  }

  await (await redisClient()).set(key, value, { EX: ttlSeconds });
}

export async function get(key: string): Promise<string | null> {
  if (getAuthConfig().sessionStore === "memory") {
    const entry = memoryState().values.get(key);
    if (!entry || entry.expiresAt <= Date.now()) {
      memoryState().values.delete(key);
      return null;
    }
    return entry.value;
  }

  return await (await redisClient()).get(key);
}

export async function take(key: string): Promise<string | null> {
  if (getAuthConfig().sessionStore === "memory") {
    const value = await get(key);
    memoryState().values.delete(key);
    return value;
  }

  return await (await redisClient()).getDel(key);
}

export async function remove(key: string): Promise<void> {
  if (getAuthConfig().sessionStore === "memory") {
    memoryState().values.delete(key);
    return;
  }

  await (await redisClient()).del(key);
}

export async function acquireLock(
  key: string,
  ttlMilliseconds: number,
): Promise<string | null> {
  const token = randomOpaqueValue(18);
  if (getAuthConfig().sessionStore === "memory") {
    const existing = memoryState().locks.get(key);
    if (existing && existing.expiresAt > Date.now()) {
      return null;
    }
    memoryState().locks.set(key, {
      expiresAt: Date.now() + ttlMilliseconds,
      token,
    });
    return token;
  }

  const result = await (await redisClient()).set(key, token, {
    NX: true,
    PX: ttlMilliseconds,
  });
  return result === "OK" ? token : null;
}

export async function releaseLock(key: string, token: string): Promise<void> {
  if (getAuthConfig().sessionStore === "memory") {
    if (memoryState().locks.get(key)?.token === token) {
      memoryState().locks.delete(key);
    }
    return;
  }

  await (await redisClient()).eval(
    "if redis.call('get', KEYS[1]) == ARGV[1] then " +
      "return redis.call('del', KEYS[1]) else return 0 end",
    { arguments: [token], keys: [key] },
  );
}

export function namespacedKey(kind: string, opaqueId: string): string {
  const digest = createHash("sha256").update(opaqueId).digest("hex");
  return "asterloom:bff:" + kind + ":" + digest;
}

async function redisClient(): Promise<AsterloomRedisClient> {
  if (globalState.asterloomRedisClient?.isReady) {
    return globalState.asterloomRedisClient;
  }
  if (globalState.asterloomRedisConnection) {
    return await globalState.asterloomRedisConnection;
  }

  const config = getAuthConfig();
  const client = createRedisClient(config.redisUrl!, config.redisPassword);
  client.on("error", (error: Error) => {
    console.error("Asterloom BFF Redis connection error:", error.message);
  });
  globalState.asterloomRedisClient = client;
  globalState.asterloomRedisConnection = client.connect().then(() => client);
  try {
    return await globalState.asterloomRedisConnection;
  } catch (error) {
    globalState.asterloomRedisConnection = undefined;
    throw error;
  }
}
