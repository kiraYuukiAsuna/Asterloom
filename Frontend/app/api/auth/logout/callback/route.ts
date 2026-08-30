import { NextResponse } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import { expiredCookieOptions } from "@/lib/auth/cookies";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export function GET() {
  const config = getAuthConfig();
  const target = new URL("/login?loggedOut=1", config.webOrigin);
  const response = NextResponse.redirect(target);
  response.cookies.set(
    config.sessionCookieName,
    "",
    expiredCookieOptions(),
  );
  response.headers.set("cache-control", "no-store");
  return response;
}
