import { z } from "zod";

import {
  AuthorizationResourceStatusObject,
  PolicyEffectObject,
  PolicySubjectTypeObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";
import { targetingRuleSchema, targetingValueSchema } from "./targeting-management";

const keyPattern = /^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/;
const timestampSchema = z.string().min(1);
const idSchema = z.string().uuid();
const versionSchema = z.number().int().positive();
const expectedVersionSchema = z.number().int().nonnegative();
const resourceStatusSchema = z.enum([
  AuthorizationResourceStatusObject.AUTHORIZATION_RESOURCE_STATUS_ACTIVE,
  AuthorizationResourceStatusObject.AUTHORIZATION_RESOURCE_STATUS_ARCHIVED,
]);
const policyEffectSchema = z.enum([
  PolicyEffectObject.POLICY_EFFECT_ALLOW,
  PolicyEffectObject.POLICY_EFFECT_DENY,
]);
const policySubjectTypeSchema = z.enum([
  PolicySubjectTypeObject.POLICY_SUBJECT_TYPE_ACTOR,
  PolicySubjectTypeObject.POLICY_SUBJECT_TYPE_ROLE,
  PolicySubjectTypeObject.POLICY_SUBJECT_TYPE_ANY,
]);

export const roleKeySchema = z
  .string()
  .trim()
  .regex(
    keyPattern,
    "Use 3–64 lowercase letters, numbers, or hyphens; start and end with a letter or number.",
  );
export const authorizationNameSchema = z.string().trim().min(1).max(200);
export const authorizationDescriptionSchema = z.string().trim().max(1000);
export const authorizationActorSchema = z.string().trim().min(1).max(200);
export const permissionKeySchema = z.string().trim().min(1).max(200);

const optionalIdSchema = z
  .union([z.literal(""), z.string().uuid()])
  .nullish()
  .transform((value) => value || undefined);
const scopeSchema = z
  .object({
    applicationId: optionalIdSchema,
    environmentId: optionalIdSchema,
    tenantId: optionalIdSchema,
  })
  .superRefine((scope, context) => {
    if (scope.environmentId && !scope.applicationId) {
      context.addIssue({
        code: "custom",
        message: "Application is required for environment scope.",
        path: ["applicationId"],
      });
    }
    if (scope.applicationId && !scope.tenantId) {
      context.addIssue({
        code: "custom",
        message: "Tenant is required for application scope.",
        path: ["tenantId"],
      });
    }
  });

const permissionSchema = z.object({
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: z.string(),
  displayName: z.string().min(1),
  id: z.union([z.literal(""), idSchema]),
  isSystem: z.boolean(),
  key: permissionKeySchema,
  module: z.string().min(1),
  scope: scopeSchema,
  status: resourceStatusSchema,
  updatedAt: timestampSchema,
  version: versionSchema,
});
const roleSchema = z.object({
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: z.string(),
  displayName: authorizationNameSchema,
  id: idSchema,
  isSystem: z.boolean(),
  key: roleKeySchema,
  permissions: z.array(permissionKeySchema),
  scope: scopeSchema,
  status: resourceStatusSchema,
  updatedAt: timestampSchema,
  version: versionSchema,
});
const roleBindingSchema = z.object({
  actorId: authorizationActorSchema,
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  id: idSchema,
  roleId: idSchema,
  roleKey: roleKeySchema,
  scope: scopeSchema,
  status: resourceStatusSchema,
  updatedAt: timestampSchema,
  version: versionSchema,
});
const policyRuleSchema = z.object({
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  effect: policyEffectSchema,
  id: idSchema,
  name: authorizationNameSchema,
  permission: permissionKeySchema,
  resourceId: z.string().max(500),
  resourceType: z.string().max(100),
  scope: scopeSchema,
  status: resourceStatusSchema,
  subject: z.string().min(1).max(200),
  subjectType: policySubjectTypeSchema,
  updatedAt: timestampSchema,
  version: versionSchema,
  condition: targetingRuleSchema.nullish(),
});
const policyRevisionSchema = z.object({
  changeSummary: z.string(),
  changeType: z.string().min(1),
  createdAt: timestampSchema,
  createdBy: z.string().min(1),
  id: idSchema,
  resourceId: z.string().min(1),
  resourceType: z.string().min(1),
  revisionNumber: z.number().int().positive(),
  snapshotHash: z.string().min(1),
});
const decisionSchema = z.object({
  allowed: z.boolean(),
  matchedPolicyIds: z.array(z.string()),
  matchedRoleKeys: z.array(z.string()),
  reason: z.string().min(1),
});

const permissionsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  permissions: z.array(permissionSchema),
});
const rolesPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  roles: z.array(roleSchema),
});
const bindingsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  roleBindings: z.array(roleBindingSchema),
});
const policiesPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  policyRules: z.array(policyRuleSchema),
});
const revisionsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  revisions: z.array(policyRevisionSchema),
});

const pageSchema = z.object({
  pageSize: z.number().int().min(1).max(100).default(50),
  pageToken: z.string().default(""),
  query: z.string().trim().max(200).default(""),
});
const permissionSelectionSchema = z
  .array(permissionKeySchema)
  .min(1, "Select at least one permission.")
  .max(200);
const roleInputSchema = z.object({
  description: authorizationDescriptionSchema,
  displayName: authorizationNameSchema,
  permissions: permissionSelectionSchema,
});
const applicationScopeSchema = scopeSchema.superRefine((scope, context) => {
  if (!scope.tenantId || !scope.applicationId || scope.environmentId) {
    context.addIssue({
      code: "custom",
      message: "Tenant and application are required; environment must be omitted.",
      path: ["applicationId"],
    });
  }
});
const permissionInputSchema = z.object({
  description: authorizationDescriptionSchema,
  displayName: authorizationNameSchema,
  key: permissionKeySchema,
  scope: applicationScopeSchema,
});
const policyInputSchema = z.object({
  condition: targetingRuleSchema.nullish(),
  effect: policyEffectSchema,
  name: authorizationNameSchema,
  permission: permissionKeySchema,
  resourceId: z.string().trim().max(500).default(""),
  resourceType: z.string().trim().max(100).default(""),
  scope: scopeSchema,
  subject: z.string().trim().min(1).max(200),
  subjectType: policySubjectTypeSchema,
});
const decisionInputSchema = z.object({
  actorId: authorizationActorSchema,
  attributes: z
    .array(z.object({ key: z.string().trim().min(1).max(64), value: targetingValueSchema }))
    .max(64)
    .default([]),
  permission: permissionKeySchema,
  resourceId: z.string().trim().max(500).default(""),
  resourceType: z.string().trim().max(100).default(""),
  scope: scopeSchema,
  trustedRoles: z.array(z.string().trim().min(1).max(200)).max(20).default([]),
});

export type PermissionRecord = z.infer<typeof permissionSchema>;
export type AuthorizationRoleRecord = z.infer<typeof roleSchema>;
export type RoleBindingRecord = z.infer<typeof roleBindingSchema>;
export type PolicyRuleRecord = z.infer<typeof policyRuleSchema>;
export type PolicyRevisionRecord = z.infer<typeof policyRevisionSchema>;
export type AuthorizationDecisionRecord = z.infer<typeof decisionSchema>;
export type AuthorizationScopeInput = z.infer<typeof scopeSchema>;
export type PolicyEffect = z.infer<typeof policyEffectSchema>;
export type PolicySubjectType = z.infer<typeof policySubjectTypeSchema>;
export type AuthorizationDecisionInput = z.input<typeof decisionInputSchema>;

export async function listPermissions(options: {
  applicationId?: string;
  includeArchived?: boolean;
  pageSize?: number;
  pageToken?: string;
  query?: string;
  tenantId?: string;
}) {
  const page = pageSchema
    .extend({
      applicationId: z.union([z.literal(""), z.string().uuid()]).default(""),
      includeArchived: z.boolean().default(false),
      tenantId: z.union([z.literal(""), z.string().uuid()]).default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.authorization.permissions.get({
    queryParameters: page,
  });
  return permissionsPageSchema.parse(requireResponse(response));
}

export async function listAllPermissions(options: {
  applicationId?: string;
  includeArchived?: boolean;
  query?: string;
  tenantId?: string;
}) {
  const permissions: PermissionRecord[] = [];
  let pageToken = "";
  do {
    const page = await listPermissions({ ...options, pageSize: 100, pageToken });
    permissions.push(...page.permissions);
    pageToken = page.nextPageToken ?? "";
  } while (pageToken);

  return { nextPageToken: "", permissions };
}

export async function createPermission(
  csrfToken: string,
  input: z.input<typeof permissionInputSchema>,
) {
  const body = permissionInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.permissions.post(
    body,
  );
  return permissionSchema.parse(requireResponse(response));
}

export async function updatePermission(
  csrfToken: string,
  permission: PermissionRecord,
  input: { description: string; displayName: string },
) {
  const body = z
    .object({
      description: authorizationDescriptionSchema,
      displayName: authorizationNameSchema,
    })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.permissions
    .byPermissionId(idSchema.parse(permission.id))
    .patch({ ...body, expectedVersion: versionSchema.parse(permission.version) });
  return permissionSchema.parse(requireResponse(response));
}

export async function archivePermission(csrfToken: string, permission: PermissionRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.permissions
    .byPermissionId(idSchema.parse(permission.id))
    .delete({ queryParameters: { expectedVersion: versionSchema.parse(permission.version) } });
  return permissionSchema.parse(requireResponse(response));
}

export async function restorePermission(csrfToken: string, permission: PermissionRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.permissions
    .withPermissionIdRestore(idSchema.parse(permission.id))
    .post({ expectedVersion: versionSchema.parse(permission.version) });
  return permissionSchema.parse(requireResponse(response));
}

export async function listRoles(options: {
  applicationId?: string;
  includeArchived?: boolean;
  pageSize?: number;
  pageToken?: string;
  query?: string;
  tenantId?: string;
}) {
  const page = pageSchema
    .extend({
      applicationId: z.union([z.literal(""), z.string().uuid()]).default(""),
      includeArchived: z.boolean().default(false),
      tenantId: z.union([z.literal(""), z.string().uuid()]).default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.authorization.roles.get({
    queryParameters: page,
  });
  return rolesPageSchema.parse(requireResponse(response));
}

export async function createRole(
  csrfToken: string,
  input: {
    description: string;
    displayName: string;
    key: string;
    permissions: string[];
    scope: AuthorizationScopeInput;
  },
) {
  const body = roleInputSchema
    .extend({ key: roleKeySchema, scope: applicationScopeSchema })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roles.post(body);
  return roleSchema.parse(requireResponse(response));
}

export async function updateRole(
  csrfToken: string,
  role: AuthorizationRoleRecord,
  input: { description: string; displayName: string; permissions: string[] },
) {
  const body = roleInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roles
    .byRoleId(idSchema.parse(role.id))
    .patch({ ...body, expectedVersion: versionSchema.parse(role.version) });
  return roleSchema.parse(requireResponse(response));
}

export async function archiveRole(csrfToken: string, role: AuthorizationRoleRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roles
    .byRoleId(idSchema.parse(role.id))
    .delete({ queryParameters: { expectedVersion: versionSchema.parse(role.version) } });
  return roleSchema.parse(requireResponse(response));
}

export async function restoreRole(csrfToken: string, role: AuthorizationRoleRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roles
    .withRoleIdRestore(idSchema.parse(role.id))
    .post({ expectedVersion: versionSchema.parse(role.version) });
  return roleSchema.parse(requireResponse(response));
}

export async function listRoleBindings(options: {
  actorId?: string;
  includeArchived?: boolean;
  pageSize?: number;
  pageToken?: string;
  tenantId?: string;
  applicationId?: string;
}) {
  const parsed = z
    .object({
      actorId: z.string().trim().max(200).default(""),
      applicationId: z.union([z.literal(""), z.string().uuid()]).default(""),
      includeArchived: z.boolean().default(false),
      pageSize: z.number().int().min(1).max(100).default(50),
      pageToken: z.string().default(""),
      tenantId: z.union([z.literal(""), z.string().uuid()]).default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.authorization.roleBindings.get({
    queryParameters: parsed,
  });
  return bindingsPageSchema.parse(requireResponse(response));
}

export async function setRoleBinding(
  csrfToken: string,
  bindingId: string,
  input: {
    actorId: string;
    expectedVersion: number;
    roleId: string;
    scope: AuthorizationScopeInput;
  },
) {
  const body = z
    .object({
      actorId: authorizationActorSchema,
      expectedVersion: expectedVersionSchema,
      roleId: idSchema,
      scope: scopeSchema,
    })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roleBindings
    .byBindingId(idSchema.parse(bindingId))
    .put(body);
  return roleBindingSchema.parse(requireResponse(response));
}

export async function removeRoleBinding(
  csrfToken: string,
  binding: RoleBindingRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.roleBindings
    .byBindingId(idSchema.parse(binding.id))
    .delete({
      queryParameters: { expectedVersion: versionSchema.parse(binding.version) },
    });
  return roleBindingSchema.parse(requireResponse(response));
}

export async function listPolicyRules(options: {
  includeArchived?: boolean;
  pageSize?: number;
  pageToken?: string;
  query?: string;
  tenantId?: string;
  applicationId?: string;
}) {
  const parsed = pageSchema
    .extend({
      includeArchived: z.boolean().default(false),
      applicationId: z.union([z.literal(""), z.string().uuid()]).default(""),
      tenantId: z.union([z.literal(""), z.string().uuid()]).default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.authorization.policies.get({
    queryParameters: parsed,
  });
  return policiesPageSchema.parse(requireResponse(response));
}

export async function createPolicyRule(
  csrfToken: string,
  input: z.input<typeof policyInputSchema>,
) {
  const body = policyInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.policies.post(
    body,
  );
  return policyRuleSchema.parse(requireResponse(response));
}

export async function updatePolicyRule(
  csrfToken: string,
  policyRule: PolicyRuleRecord,
  input: z.input<typeof policyInputSchema>,
) {
  const body = policyInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.policies
    .byPolicyRuleId(idSchema.parse(policyRule.id))
    .patch({ ...body, expectedVersion: versionSchema.parse(policyRule.version) });
  return policyRuleSchema.parse(requireResponse(response));
}

export async function archivePolicyRule(
  csrfToken: string,
  policyRule: PolicyRuleRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.policies
    .byPolicyRuleId(idSchema.parse(policyRule.id))
    .delete({
      queryParameters: { expectedVersion: versionSchema.parse(policyRule.version) },
    });
  return policyRuleSchema.parse(requireResponse(response));
}

export async function restorePolicyRule(
  csrfToken: string,
  policyRule: PolicyRuleRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorization.policies
    .withPolicyRuleIdRestore(idSchema.parse(policyRule.id))
    .post({ expectedVersion: versionSchema.parse(policyRule.version) });
  return policyRuleSchema.parse(requireResponse(response));
}

export async function listPolicyRevisions(options: {
  pageSize?: number;
  pageToken?: string;
  resourceId?: string;
  resourceType?: string;
}) {
  const parsed = z
    .object({
      pageSize: z.number().int().min(1).max(100).default(50),
      pageToken: z.string().default(""),
      resourceId: z.string().trim().max(200).default(""),
      resourceType: z.string().trim().max(64).default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.authorization.revisions.get({
    queryParameters: parsed,
  });
  return revisionsPageSchema.parse(requireResponse(response));
}

export async function simulateAuthorization(
  csrfToken: string,
  input: z.input<typeof decisionInputSchema>,
) {
  const body = decisionInputSchema.parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorizationSimulate.post({
    input: body,
  });
  return decisionSchema.parse(requireResponse(response));
}

export async function checkCurrentActorPermission(
  csrfToken: string,
  input: Omit<z.input<typeof decisionInputSchema>, "trustedRoles">,
) {
  const body = decisionInputSchema.parse({ ...input, trustedRoles: [] });
  const response = await getAsterloomApiClient(csrfToken).api.v1.authorizationCheck.post(body);
  return decisionSchema.parse(requireResponse(response));
}

export function authorizationErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    const issue = error.issues[0];
    if (!issue) return "The submitted values are invalid.";
    const path = issue.path.length > 0 ? `${issue.path.join(".")}: ` : "";
    return path + issue.message;
  }

  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") {
      return candidate.messageEscaped;
    }
    if (typeof candidate.message === "string") {
      return candidate.message;
    }
  }

  return "The authorization operation could not be completed.";
}

export function isAuthorizationVersionConflict(error: unknown): boolean {
  return authorizationErrorMessage(error).includes("changed since it was loaded");
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The authorization API returned an empty response.");
  }
  return response;
}
