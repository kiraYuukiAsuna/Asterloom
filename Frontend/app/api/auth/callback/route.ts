import { NextRequest, NextResponse } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import {
  expiredCookieOptions,
  sessionCookieOptions,
} from "@/lib/auth/cookies";
import { safeEqual } from "@/lib/auth/crypto";
import {
  createSession,
  saveSession,
  takeLoginTransaction,
} from "@/lib/auth/session";
import { exchangeAuthorizationCode } from "@/lib/auth/token-client";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const config = getAuthConfig();
  const state = request.nextUrl.searchParams.get("state");
  const stateCookie = request.cookies.get(config.oidcCookieName)?.value;
  const code = request.nextUrl.searchParams.get("code");
  if (
    !state ||
    !stateCookie ||
    !safeEqual(state, stateCookie) ||
    request.nextUrl.searchParams.has("error")
  ) {
    return failedCallback("invalid_response");
  }

  const transaction = await takeLoginTransaction(state);
  if (!transaction || !code) {
    return failedCallback("expired_state");
  }

  try {
    const tokens = await exchangeAuthorizationCode({
      code,
      codeVerifier: transaction.codeVerifier,
      nonce: transaction.nonce,
      redirectUri: transaction.redirectUri,
    });
    if (!tokens.actor) {
      return failedCallback("invalid_identity");
    }

    const persistent = tokens.persistentSession === true;
    const session = createSession(
      { ...tokens, actor: tokens.actor },
      persistent,
    );
    await saveSession(session.id, session.record);
    const response = NextResponse.redirect(
      new URL(transaction.returnTo, config.webOrigin),
    );
    response.cookies.set(
      config.sessionCookieName,
      session.id,
      sessionCookieOptions(persistent),
    );
    response.cookies.set(
      config.oidcCookieName,
      "",
      expiredCookieOptions(),
    );
    response.headers.set("cache-control", "no-store");
    return response;
  } catch {
    return failedCallback("passport_unavailable");
  }
}

function failedCallback(code: string): NextResponse {
  const config = getAuthConfig();
  const target = new URL("/login", config.webOrigin);
  target.searchParams.set("error", code);
  const response = NextResponse.redirect(target);
  response.cookies.set(
    config.oidcCookieName,
    "",
    expiredCookieOptions(),
  );
  response.headers.set("cache-control", "no-store");
  return response;
}
