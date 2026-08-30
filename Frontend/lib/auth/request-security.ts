import "server-only";

import type { NextRequest } from "next/server";

import { getAuthConfig } from "@/lib/auth/config";
import { safeEqual } from "@/lib/auth/crypto";
import type { BffSession } from "@/lib/auth/types";

export function isMutation(method: string): boolean {
  return !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
}

export function validateMutationRequest(
  request: NextRequest,
  session: BffSession,
): boolean {
  if (!isMutation(request.method)) {
    return true;
  }
  const origin = request.headers.get("origin");
  const token = request.headers.get("x-csrf-token");
  return (
    origin === getAuthConfig().webOrigin &&
    token !== null &&
    safeEqual(token, session.csrfToken)
  );
}
