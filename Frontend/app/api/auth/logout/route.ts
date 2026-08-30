import { NextRequest, NextResponse } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import { expiredCookieOptions } from "@/lib/auth/cookies";
import { deleteSession, readSession } from "@/lib/auth/session";
import { validateMutationRequest } from "@/lib/auth/request-security";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function POST(request: NextRequest) {
  const config = getAuthConfig();
  const id = request.cookies.get(config.sessionCookieName)?.value;
  const session = id ? await readSession(id) : null;
  if (!id || !session) {
    return Response.json({ code: "UNAUTHENTICATED" }, { status: 401 });
  }
  if (!validateMutationRequest(request, session)) {
    return Response.json({ code: "CSRF_REJECTED" }, { status: 403 });
  }

  await deleteSession(id);
  const logoutUrl = new URL("/connect/logout", config.passportPublicUrl);
  if (session.idToken) {
    logoutUrl.searchParams.set("id_token_hint", session.idToken);
  }
  logoutUrl.searchParams.set(
    "post_logout_redirect_uri",
    config.webOrigin + "/api/auth/logout/callback",
  );

  const response = NextResponse.json({ logoutUrl: logoutUrl.toString() });
  response.cookies.set(
    config.sessionCookieName,
    "",
    expiredCookieOptions(),
  );
  response.headers.set("cache-control", "no-store");
  return response;
}
