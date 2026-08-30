import "server-only";

import { z } from "zod";

export const actorSchema = z.object({
  email: z.string().email().optional(),
  name: z.string().min(1).max(200),
  roles: z.array(z.string().min(1).max(200)).max(100),
  subject: z.string().min(1).max(200),
});

export const sessionSchema = z.object({
  absoluteExpiresAt: z.number().int().positive(),
  accessExpiresAt: z.number().int().positive(),
  accessToken: z.string().min(1),
  actor: actorSchema,
  csrfToken: z.string().min(32).max(200),
  idToken: z.string().min(1).optional(),
  refreshToken: z.string().min(1).optional(),
});

export const loginTransactionSchema = z.object({
  codeVerifier: z.string().min(43).max(128),
  nonce: z.string().min(32).max(200),
  redirectUri: z.string().url(),
  returnTo: z.string().startsWith("/"),
});

export type Actor = z.infer<typeof actorSchema>;
export type BffSession = z.infer<typeof sessionSchema>;
export type LoginTransaction = z.infer<typeof loginTransactionSchema>;
