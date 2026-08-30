import { NextRequest } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import { ensureFreshSession, readSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const config = getAuthConfig();
  const id = request.cookies.get(config.sessionCookieName)?.value;
  const existing = id ? await readSession(id) : null;
  const session = id && existing
    ? await ensureFreshSession(id, existing)
    : null;
  if (!session) {
    return Response.json(
      { authenticated: false },
      { status: 401, headers: { "cache-control": "no-store" } },
    );
  }

  return Response.json(
    {
      actor: session.actor,
      authenticated: true,
      csrfToken: session.csrfToken,
      expiresAt: new Date(session.absoluteExpiresAt).toISOString(),
    },
    { headers: { "cache-control": "no-store" } },
  );
}
