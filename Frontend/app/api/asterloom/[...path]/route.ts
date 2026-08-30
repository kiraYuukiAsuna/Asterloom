import { NextRequest } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import { validateMutationRequest } from "@/lib/auth/request-security";
import {
  ensureFreshSession,
  readSession,
  SessionStoreUnavailableError,
} from "@/lib/auth/session";
import type { BffSession } from "@/lib/auth/types";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

type RouteContext = {
  params: Promise<{ path: string[] }>;
};

const requestHeaderAllowList = new Set([
  "accept",
  "content-type",
  "if-match",
  "if-none-match",
  "x-request-id",
]);

const responseHeaderBlockList = new Set([
  "connection",
  "content-encoding",
  "content-length",
  "keep-alive",
  "set-cookie",
  "transfer-encoding",
]);

async function forward(request: NextRequest, context: RouteContext) {
  const config = getAuthConfig();
  const sessionId = request.cookies.get(config.sessionCookieName)?.value;
  const storedSession = sessionId ? await readSession(sessionId) : null;
  if (!sessionId || !storedSession) {
    return authError("UNAUTHENTICATED", 401);
  }
  if (!validateMutationRequest(request, storedSession)) {
    return authError("CSRF_REJECTED", 403);
  }

  let session: BffSession | null;
  try {
    session = await ensureFreshSession(sessionId, storedSession);
  } catch (error) {
    if (error instanceof SessionStoreUnavailableError) {
      return authError("SESSION_SERVICE_UNAVAILABLE", 503);
    }
    throw error;
  }
  if (!session) {
    return authError("UNAUTHENTICATED", 401);
  }

  const { path } = await context.params;
  const safePath = path.map((segment) => encodeURIComponent(segment)).join("/");
  const target = new URL(
    safePath,
    config.backendUrl + "/",
  );
  target.search = request.nextUrl.search;

  const headers = new Headers();
  for (const [name, value] of request.headers) {
    if (requestHeaderAllowList.has(name.toLowerCase())) {
      headers.set(name, value);
    }
  }

  const requestId = headers.get("x-request-id") ?? crypto.randomUUID();
  headers.set("x-request-id", requestId);
  headers.set("authorization", "Bearer " + session.accessToken);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  const body = hasBody ? await request.arrayBuffer() : undefined;

  try {
    let upstream = await fetchUpstream(request, target, headers, body);
    if (upstream.status === 401 && session.refreshToken) {
      const refreshed = await ensureFreshSession(sessionId, session, true);
      if (refreshed) {
        headers.set("authorization", "Bearer " + refreshed.accessToken);
        upstream = await fetchUpstream(request, target, headers, body);
      }
    }

    const responseHeaders = new Headers();
    for (const [name, value] of upstream.headers) {
      if (!responseHeaderBlockList.has(name.toLowerCase())) {
        responseHeaders.append(name, value);
      }
    }
    responseHeaders.set("x-request-id", requestId);

    return new Response(upstream.body, {
      status: upstream.status,
      statusText: upstream.statusText,
      headers: responseHeaders,
    });
  } catch {
    return Response.json(
      {
        code: "BACKEND_UNAVAILABLE",
        message: "Asterloom Server is currently unavailable.",
        requestId,
      },
      {
        status: 502,
        headers: {
          "cache-control": "no-store",
          "x-request-id": requestId,
        },
      },
    );
  }
}

function fetchUpstream(
  request: NextRequest,
  target: URL,
  headers: Headers,
  body: ArrayBuffer | undefined,
) {
  return fetch(target, {
    body,
    cache: "no-store",
    headers,
    method: request.method,
    redirect: "manual",
    signal: AbortSignal.timeout(30_000),
  });
}

function authError(code: string, status: number) {
  return Response.json(
    { code, message: "The BFF could not authorize this request." },
    { status, headers: { "cache-control": "no-store" } },
  );
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
