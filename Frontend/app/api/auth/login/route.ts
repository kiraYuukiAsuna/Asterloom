import { createHash } from "node:crypto";
import { NextRequest, NextResponse } from "next/server";

import { getAuthConfig, safeReturnTo } from "@/lib/auth/config";
import { oidcCookieOptions } from "@/lib/auth/cookies";
import { randomOpaqueValue } from "@/lib/auth/crypto";
import { saveLoginTransaction } from "@/lib/auth/session";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const config = getAuthConfig();
  const state = randomOpaqueValue();
  const nonce = randomOpaqueValue();
  const codeVerifier = randomOpaqueValue(48);
  const codeChallenge = createHash("sha256")
    .update(codeVerifier)
    .digest("base64url");
  const redirectUri = config.webOrigin + "/api/auth/callback";
  const returnTo = safeReturnTo(request.nextUrl.searchParams.get("returnTo"));
  const requestedLocale =
    request.nextUrl.searchParams.get("locale") ??
    request.cookies.get("asterloom-locale")?.value;
  const uiLocale = requestedLocale?.toLowerCase().startsWith("zh")
    ? "zh-CN"
    : "en";

  await saveLoginTransaction(state, {
    codeVerifier,
    nonce,
    redirectUri,
    returnTo,
  });

  const authorizationUrl = new URL(
    "/connect/authorize",
    config.passportPublicUrl,
  );
  authorizationUrl.search = new URLSearchParams({
    client_id: config.clientId,
    code_challenge: codeChallenge,
    code_challenge_method: "S256",
    nonce,
    redirect_uri: redirectUri,
    response_type: "code",
    scope: "openid profile email roles offline_access asterloom.api",
    state,
    ui_locales: uiLocale,
  }).toString();

  const response = NextResponse.redirect(authorizationUrl);
  response.cookies.set(
    config.oidcCookieName,
    state,
    oidcCookieOptions(),
  );
  response.headers.set("cache-control", "no-store");
  return response;
}
