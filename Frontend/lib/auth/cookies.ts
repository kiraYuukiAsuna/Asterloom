import "server-only";

import type { ResponseCookie } from "next/dist/compiled/@edge-runtime/cookies";

import { getAuthConfig } from "@/lib/auth/config";
import {
  oidcCookieMaxAge,
  sessionCookieMaxAge,
} from "@/lib/auth/session";

export function sessionCookieOptions(): Partial<ResponseCookie> {
  return {
    httpOnly: true,
    maxAge: sessionCookieMaxAge,
    path: "/",
    priority: "high",
    sameSite: "lax",
    secure: getAuthConfig().secureCookies,
  };
}

export function oidcCookieOptions(): Partial<ResponseCookie> {
  return {
    httpOnly: true,
    maxAge: oidcCookieMaxAge,
    path: "/",
    priority: "high",
    sameSite: "lax",
    secure: getAuthConfig().secureCookies,
  };
}

export function expiredCookieOptions(): Partial<ResponseCookie> {
  return {
    httpOnly: true,
    maxAge: 0,
    path: "/",
    sameSite: "lax",
    secure: getAuthConfig().secureCookies,
  };
}
