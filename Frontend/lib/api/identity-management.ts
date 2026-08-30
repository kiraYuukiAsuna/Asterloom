import { z } from "zod";

import {
  IdentitySessionStatusObject,
  IdentityUserStatusObject,
  OidcApplicationTypeObject,
  OidcClientTypeObject,
  OidcGrantTypeObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

export const passportRoles = [
  "SuperAdministrator",
  "TenantAdministrator",
  "Operator",
  "Developer",
  "Viewer",
] as const;

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const userVersionSchema = z.number().int().positive();
const opaqueVersionSchema = z.string().min(1);
const pageSchema = z.object({
  pageSize: z.number().int().min(1).max(100).default(100),
  pageToken: z.string().default(""),
  query: z.string().trim().max(200).default(""),
});
const roleSchema = z.enum(passportRoles);
const rolesSchema = z.array(roleSchema).min(1).max(passportRoles.length);
const userStatusSchema = z.enum([
  IdentityUserStatusObject.IDENTITY_USER_STATUS_PENDING,
  IdentityUserStatusObject.IDENTITY_USER_STATUS_ACTIVE,
  IdentityUserStatusObject.IDENTITY_USER_STATUS_SUSPENDED,
  IdentityUserStatusObject.IDENTITY_USER_STATUS_ARCHIVED,
]);
const sessionStatusSchema = z.enum([
  IdentitySessionStatusObject.IDENTITY_SESSION_STATUS_VALID,
  IdentitySessionStatusObject.IDENTITY_SESSION_STATUS_REVOKED,
]);
const clientTypeSchema = z.enum([
  OidcClientTypeObject.OIDC_CLIENT_TYPE_PUBLIC,
  OidcClientTypeObject.OIDC_CLIENT_TYPE_CONFIDENTIAL,
]);
const applicationTypeSchema = z.enum([
  OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_WEB,
  OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_NATIVE,
]);
const grantTypeSchema = z.enum([
  OidcGrantTypeObject.OIDC_GRANT_TYPE_AUTHORIZATION_CODE,
  OidcGrantTypeObject.OIDC_GRANT_TYPE_CLIENT_CREDENTIALS,
  OidcGrantTypeObject.OIDC_GRANT_TYPE_REFRESH_TOKEN,
]);
const uriSchema = z.url().refine((value) => {
  const url = new URL(value);
  return !url.hash;
}, "Use an absolute URI without a fragment.");
const scopeNameSchema = z
  .string()
  .trim()
  .regex(/^[a-z][a-z0-9._:-]{1,99}$/);
const clientIdSchema = z
  .string()
  .trim()
  .regex(/^[a-z][a-z0-9._-]{1,99}$/);
const displayNameSchema = z.string().trim().min(1).max(200);
const descriptionSchema = z.string().trim().max(1000);

const userSchema = z.object({
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  displayName: displayNameSchema,
  email: z.email(),
  id: idSchema,
  roles: z.array(roleSchema),
  status: userStatusSchema,
  updatedAt: timestampSchema,
  version: userVersionSchema,
});
const invitationSchema = z.object({
  expiresAt: timestampSchema,
  invitationUrl: z.url(),
  user: userSchema,
});
const sessionSchema = z.object({
  clientDisplayName: z.string(),
  clientId: z.string(),
  createdAt: timestampSchema,
  id: z.string().min(1),
  scopes: z.array(z.string()),
  status: sessionStatusSchema,
  userId: idSchema,
});
const clientSchema = z.object({
  applicationType: applicationTypeSchema,
  clientId: clientIdSchema,
  clientType: clientTypeSchema,
  displayName: displayNameSchema,
  grantTypes: z.array(grantTypeSchema).min(1),
  id: z.string().min(1),
  postLogoutRedirectUris: z.array(uriSchema),
  redirectUris: z.array(uriSchema),
  scopes: z.array(scopeNameSchema),
  version: opaqueVersionSchema,
});
const credentialSchema = z.object({
  client: clientSchema,
  clientSecret: z.string(),
});
const scopeSchema = z.object({
  description: z.string(),
  displayName: displayNameSchema,
  id: z.string().min(1),
  name: scopeNameSchema,
  resources: z.array(z.string().min(1).max(200)),
  version: opaqueVersionSchema,
});
const usersPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  users: z.array(userSchema),
});
const sessionsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  sessions: z.array(sessionSchema),
});
const clientsPageSchema = z.object({
  clients: z.array(clientSchema),
  nextPageToken: z.string().nullish(),
});
const scopesPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  scopes: z.array(scopeSchema),
});
const clientConfigurationShape = {
  displayName: displayNameSchema,
  grantTypes: z.array(grantTypeSchema).min(1),
  postLogoutRedirectUris: z.array(uriSchema).max(20),
  redirectUris: z.array(uriSchema).max(20),
  scopes: z.array(scopeNameSchema).max(100),
};
const clientConfigurationSchema = z
  .object(clientConfigurationShape)
  .superRefine((client, context) => {
    const hasAuthorizationCode = client.grantTypes.includes(
      OidcGrantTypeObject.OIDC_GRANT_TYPE_AUTHORIZATION_CODE,
    );
    if (hasAuthorizationCode && client.redirectUris.length === 0) {
      context.addIssue({
        code: "custom",
        message: "Authorization-code clients require a redirect URI.",
        path: ["redirectUris"],
      });
    }
  });
const createClientInputSchema = z
  .object({
    applicationType: applicationTypeSchema,
    clientId: clientIdSchema,
    clientType: clientTypeSchema,
    ...clientConfigurationShape,
  })
  .superRefine((client, context) => {
    if (
      client.grantTypes.includes(
        OidcGrantTypeObject.OIDC_GRANT_TYPE_AUTHORIZATION_CODE,
      ) &&
      client.redirectUris.length === 0
    ) {
      context.addIssue({
        code: "custom",
        message: "Authorization-code clients require a redirect URI.",
        path: ["redirectUris"],
      });
    }
    if (
      client.grantTypes.includes(
        OidcGrantTypeObject.OIDC_GRANT_TYPE_CLIENT_CREDENTIALS,
      ) &&
      client.clientType !== OidcClientTypeObject.OIDC_CLIENT_TYPE_CONFIDENTIAL
    ) {
      context.addIssue({
        code: "custom",
        message: "Client credentials require a confidential client.",
        path: ["clientType"],
      });
    }
    if (
      client.applicationType === OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_NATIVE &&
      client.clientType !== OidcClientTypeObject.OIDC_CLIENT_TYPE_PUBLIC
    ) {
      context.addIssue({
        code: "custom",
        message: "Native applications must use a public client with PKCE.",
        path: ["clientType"],
      });
    }
    if (
      client.applicationType === OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_NATIVE &&
      !client.grantTypes.includes(
        OidcGrantTypeObject.OIDC_GRANT_TYPE_AUTHORIZATION_CODE,
      )
    ) {
      context.addIssue({
        code: "custom",
        message: "Native applications require authorization code + PKCE.",
        path: ["grantTypes"],
      });
    }
  });
const scopeInputSchema = z.object({
  description: descriptionSchema,
  displayName: displayNameSchema,
  name: scopeNameSchema.optional(),
  resources: z.array(z.string().trim().min(1).max(200)).max(100),
});

export type IdentityUserRecord = z.infer<typeof userSchema>;
export type IdentitySessionRecord = z.infer<typeof sessionSchema>;
export type OidcClientRecord = z.infer<typeof clientSchema>;
export type OidcScopeRecord = z.infer<typeof scopeSchema>;
export type UserInvitationRecord = z.infer<typeof invitationSchema>;
export type PassportRole = z.infer<typeof roleSchema>;
export type IdentityUserStatus = z.infer<typeof userStatusSchema>;
export type OidcClientType = z.infer<typeof clientTypeSchema>;
export type OidcApplicationType = z.infer<typeof applicationTypeSchema>;
export type OidcGrantType = z.infer<typeof grantTypeSchema>;

export async function listUsers(options: {
  includeArchived?: boolean;
  pageSize?: number;
  pageToken?: string;
  query?: string;
}) {
  const queryParameters = pageSchema
    .extend({ includeArchived: z.boolean().default(false) })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.identity.users.get({
    queryParameters,
  });
  return usersPageSchema.parse(requireResponse(response));
}

export async function getUser(userId: string) {
  const response = await getAsterloomApiClient().api.v1.identity.users
    .byUserId(idSchema.parse(userId))
    .get();
  return userSchema.parse(requireResponse(response));
}

export async function inviteUser(
  csrfToken: string,
  input: { displayName: string; email: string; roles: PassportRole[] },
) {
  const body = z
    .object({
      displayName: displayNameSchema,
      email: z.email(),
      roles: rolesSchema,
    })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.usersInvite.post(
    body,
  );
  return invitationSchema.parse(requireResponse(response));
}

export async function resendInvitation(
  csrfToken: string,
  user: IdentityUserRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .withUserIdResendInvitation(idSchema.parse(user.id))
    .post({ expectedVersion: userVersionSchema.parse(user.version) });
  return invitationSchema.parse(requireResponse(response));
}

export async function updateUser(
  csrfToken: string,
  user: IdentityUserRecord,
  displayName: string,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .byUserId(idSchema.parse(user.id))
    .patch({
      displayName: displayNameSchema.parse(displayName),
      expectedVersion: userVersionSchema.parse(user.version),
    });
  return userSchema.parse(requireResponse(response));
}

export async function setUserRoles(
  csrfToken: string,
  user: IdentityUserRecord,
  roles: PassportRole[],
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .byUserId(idSchema.parse(user.id))
    .roles.put({
      expectedVersion: userVersionSchema.parse(user.version),
      roles: rolesSchema.parse(roles),
    });
  return userSchema.parse(requireResponse(response));
}

export async function suspendUser(csrfToken: string, user: IdentityUserRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .withUserIdSuspend(idSchema.parse(user.id))
    .post({ expectedVersion: userVersionSchema.parse(user.version) });
  return userSchema.parse(requireResponse(response));
}

export async function reactivateUser(csrfToken: string, user: IdentityUserRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .withUserIdReactivate(idSchema.parse(user.id))
    .post({ expectedVersion: userVersionSchema.parse(user.version) });
  return userSchema.parse(requireResponse(response));
}

export async function archiveUser(csrfToken: string, user: IdentityUserRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .byUserId(idSchema.parse(user.id))
    .delete({ queryParameters: { expectedVersion: userVersionSchema.parse(user.version) } });
  return userSchema.parse(requireResponse(response));
}

export async function restoreUser(csrfToken: string, user: IdentityUserRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .withUserIdRestore(idSchema.parse(user.id))
    .post({ expectedVersion: userVersionSchema.parse(user.version) });
  return userSchema.parse(requireResponse(response));
}

export async function listUserSessions(
  userId: string,
  options: { includeRevoked?: boolean; pageSize?: number; pageToken?: string },
) {
  const queryParameters = z
    .object({
      includeRevoked: z.boolean().default(true),
      pageSize: z.number().int().min(1).max(100).default(100),
      pageToken: z.string().default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.identity.users
    .byUserId(idSchema.parse(userId))
    .sessions.get({ queryParameters });
  return sessionsPageSchema.parse(requireResponse(response));
}

export async function revokeUserSession(
  csrfToken: string,
  userId: string,
  sessionId: string,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .byUserId(idSchema.parse(userId))
    .sessions.bySessionId(z.string().min(1).parse(sessionId))
    .delete();
  return sessionSchema.parse(requireResponse(response));
}

export async function revokeAllUserSessions(csrfToken: string, userId: string) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.users
    .byUserId(idSchema.parse(userId))
    .sessionsRevokeAll.post({});
  return z
    .object({ revokedSessions: z.number().int().nonnegative() })
    .parse(requireResponse(response));
}

export async function listClients(options: {
  pageSize?: number;
  pageToken?: string;
  query?: string;
}) {
  const response = await getAsterloomApiClient().api.v1.identity.clients.get({
    queryParameters: pageSchema.parse(options),
  });
  return clientsPageSchema.parse(requireResponse(response));
}

export async function getClient(clientId: string) {
  const response = await getAsterloomApiClient().api.v1.identity.clients
    .byClientId(clientIdSchema.parse(clientId))
    .get();
  return clientSchema.parse(requireResponse(response));
}

export async function createClient(
  csrfToken: string,
  input: {
    applicationType: OidcApplicationType;
    clientId: string;
    clientType: OidcClientType;
    displayName: string;
    grantTypes: OidcGrantType[];
    postLogoutRedirectUris: string[];
    redirectUris: string[];
    scopes: string[];
  },
) {
  const body = createClientInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.clients.post(
    body,
  );
  return credentialSchema.parse(requireResponse(response));
}

export async function updateClient(
  csrfToken: string,
  client: OidcClientRecord,
  input: {
    displayName: string;
    grantTypes: OidcGrantType[];
    postLogoutRedirectUris: string[];
    redirectUris: string[];
    scopes: string[];
  },
) {
  const body = clientConfigurationSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.clients
    .byClientId(clientIdSchema.parse(client.clientId))
    .patch({ ...body, expectedVersion: opaqueVersionSchema.parse(client.version) });
  return clientSchema.parse(requireResponse(response));
}

export async function rotateClientSecret(
  csrfToken: string,
  client: OidcClientRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.clients
    .withClientIdRotateSecret(clientIdSchema.parse(client.clientId))
    .post({ expectedVersion: opaqueVersionSchema.parse(client.version) });
  return credentialSchema.parse(requireResponse(response));
}

export async function deleteClient(csrfToken: string, client: OidcClientRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.clients
    .byClientId(clientIdSchema.parse(client.clientId))
    .delete({ queryParameters: { expectedVersion: opaqueVersionSchema.parse(client.version) } });
  return clientSchema.parse(requireResponse(response));
}

export async function listScopes(options: {
  pageSize?: number;
  pageToken?: string;
  query?: string;
}) {
  const response = await getAsterloomApiClient().api.v1.identity.scopes.get({
    queryParameters: pageSchema.parse(options),
  });
  return scopesPageSchema.parse(requireResponse(response));
}

export async function getScope(scopeId: string) {
  const response = await getAsterloomApiClient().api.v1.identity.scopes
    .byScopeId(z.string().min(1).parse(scopeId))
    .get();
  return scopeSchema.parse(requireResponse(response));
}

export async function createScope(
  csrfToken: string,
  input: { description: string; displayName: string; name: string; resources: string[] },
) {
  const body = scopeInputSchema.extend({ name: scopeNameSchema }).parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.scopes.post(
    body,
  );
  return scopeSchema.parse(requireResponse(response));
}

export async function updateScope(
  csrfToken: string,
  scope: OidcScopeRecord,
  input: { description: string; displayName: string; resources: string[] },
) {
  const body = scopeInputSchema.omit({ name: true }).parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.scopes
    .byScopeId(z.string().min(1).parse(scope.id))
    .patch({ ...body, expectedVersion: opaqueVersionSchema.parse(scope.version) });
  return scopeSchema.parse(requireResponse(response));
}

export async function deleteScope(csrfToken: string, scope: OidcScopeRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.identity.scopes
    .byScopeId(z.string().min(1).parse(scope.id))
    .delete({ queryParameters: { expectedVersion: opaqueVersionSchema.parse(scope.version) } });
  return scopeSchema.parse(requireResponse(response));
}

export function identityErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    const issue = error.issues[0];
    if (!issue) return "The submitted values are invalid.";
    const path = issue.path.length > 0 ? `${issue.path.join(".")}: ` : "";
    return path + issue.message;
  }

  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }

  return "The identity operation could not be completed.";
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The identity API returned an empty response.");
  }
  return response;
}
