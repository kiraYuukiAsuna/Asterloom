import "server-only";

import { createRemoteJWKSet, jwtVerify, type JWTPayload } from "jose";
import { z } from "zod";

import { getAuthConfig } from "@/lib/auth/config";
import type { Actor } from "@/lib/auth/types";

const tokenResponseSchema = z.object({
  access_token: z.string().min(1),
  expires_in: z.number().int().positive(),
  id_token: z.string().min(1).optional(),
  refresh_token: z.string().min(1).optional(),
  scope: z.string().optional(),
  token_type: z.string().min(1),
});

export type ValidatedTokens = {
  accessExpiresAt: number;
  accessToken: string;
  actor?: Actor;
  idToken?: string;
  persistentSession?: boolean;
  refreshToken?: string;
};

export class OidcTokenError extends Error {
  constructor(
    message: string,
    public readonly permanent: boolean,
  ) {
    super(message);
    this.name = "OidcTokenError";
  }
}

const jwksByUrl = new Map<
  string,
  ReturnType<typeof createRemoteJWKSet>
>();

export async function exchangeAuthorizationCode(input: {
  code: string;
  codeVerifier: string;
  nonce: string;
  redirectUri: string;
}): Promise<ValidatedTokens> {
  const response = await tokenRequest({
    code: input.code,
    code_verifier: input.codeVerifier,
    grant_type: "authorization_code",
    redirect_uri: input.redirectUri,
  });
  if (!response.id_token) {
    throw new OidcTokenError("The OIDC response did not contain an ID token.", true);
  }

  const identity = await verifyIdToken(response.id_token, input.nonce);
  return {
    accessExpiresAt: Date.now() + response.expires_in * 1000,
    accessToken: response.access_token,
    actor: identity.actor,
    idToken: response.id_token,
    persistentSession: identity.persistentSession,
    refreshToken: response.refresh_token,
  };
}

export async function exchangeRefreshToken(
  refreshToken: string,
): Promise<ValidatedTokens> {
  const response = await tokenRequest({
    grant_type: "refresh_token",
    refresh_token: refreshToken,
  });
  const identity = response.id_token
    ? await verifyIdToken(response.id_token)
    : undefined;
  return {
    accessExpiresAt: Date.now() + response.expires_in * 1000,
    accessToken: response.access_token,
    actor: identity?.actor,
    idToken: response.id_token,
    persistentSession: identity?.persistentSession,
    refreshToken: response.refresh_token,
  };
}

async function tokenRequest(
  values: Record<string, string>,
): Promise<z.infer<typeof tokenResponseSchema>> {
  const config = getAuthConfig();
  const body = new URLSearchParams({ client_id: config.clientId, ...values });
  if (config.clientSecret) {
    body.set("client_secret", config.clientSecret);
  }

  let response: Response;
  try {
    response = await fetch(config.passportPublicUrl + "/connect/token", {
      body,
      cache: "no-store",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      method: "POST",
      signal: AbortSignal.timeout(10_000),
    });
  } catch {
    throw new OidcTokenError("Passport is temporarily unavailable.", false);
  }

  if (!response.ok) {
    throw new OidcTokenError(
      "Passport rejected the token request.",
      response.status >= 400 && response.status < 500,
    );
  }

  try {
    return tokenResponseSchema.parse(await response.json());
  } catch {
    throw new OidcTokenError("Passport returned an invalid token response.", true);
  }
}

async function verifyIdToken(
  idToken: string,
  expectedNonce?: string,
): Promise<{ actor: Actor; persistentSession: boolean }> {
  const config = getAuthConfig();
  const jwksUrl = config.passportPublicUrl + "/.well-known/jwks";
  let jwks = jwksByUrl.get(jwksUrl);
  if (!jwks) {
    jwks = createRemoteJWKSet(new URL(jwksUrl), {
      cacheMaxAge: 10 * 60 * 1000,
      cooldownDuration: 30_000,
      timeoutDuration: 5_000,
    });
    jwksByUrl.set(jwksUrl, jwks);
  }

  let payload: JWTPayload;
  try {
    ({ payload } = await jwtVerify(idToken, jwks, {
      algorithms: ["RS256"],
      audience: config.clientId,
      issuer: ensureTrailingSlash(config.issuer),
    }));
  } catch {
    throw new OidcTokenError("Passport returned an invalid ID token.", true);
  }

  if (expectedNonce !== undefined && payload.nonce !== expectedNonce) {
    throw new OidcTokenError("The OIDC nonce did not match.", true);
  }
  if (typeof payload.sub !== "string" || payload.sub.length === 0) {
    throw new OidcTokenError("The ID token did not contain a subject.", true);
  }

  const rawRoles = payload.role;
  const roles = Array.isArray(rawRoles)
    ? rawRoles.filter((role): role is string => typeof role === "string")
    : typeof rawRoles === "string"
      ? [rawRoles]
      : [];
  const name =
    typeof payload.name === "string" && payload.name
      ? payload.name
      : typeof payload.preferred_username === "string" &&
          payload.preferred_username
        ? payload.preferred_username
        : payload.sub;

  return {
    actor: {
      email: typeof payload.email === "string" ? payload.email : undefined,
      name,
      roles,
      subject: payload.sub,
    },
    persistentSession:
      typeof payload.asterloom_persistent_session === "string" &&
      payload.asterloom_persistent_session.toLowerCase() === "true",
  };
}

function ensureTrailingSlash(value: string): string {
  return value.endsWith("/") ? value : value + "/";
}
