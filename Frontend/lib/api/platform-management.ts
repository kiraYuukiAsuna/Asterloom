import { z } from "zod";

import {
  EnvironmentTypeObject,
  MembershipStatusObject,
  ResourceStatusObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const slugPattern = /^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/;
const resourceStatusSchema = z.enum([
  ResourceStatusObject.RESOURCE_STATUS_ACTIVE,
  ResourceStatusObject.RESOURCE_STATUS_ARCHIVED,
]);
const environmentTypeSchema = z.enum([
  EnvironmentTypeObject.ENVIRONMENT_TYPE_DEVELOPMENT,
  EnvironmentTypeObject.ENVIRONMENT_TYPE_STAGING,
  EnvironmentTypeObject.ENVIRONMENT_TYPE_PRODUCTION,
]);
const membershipStatusSchema = z.enum([
  MembershipStatusObject.MEMBERSHIP_STATUS_ACTIVE,
  MembershipStatusObject.MEMBERSHIP_STATUS_REMOVED,
]);
const timestampSchema = z.string().min(1);

export const slugSchema = z
  .string()
  .trim()
  .regex(
    slugPattern,
    "Use 3–64 lowercase letters, numbers, or hyphens; start and end with a letter or number.",
  );
export const displayNameSchema = z.string().trim().min(1).max(200);
export const actorIdSchema = z.string().trim().uuid();

const tenantSchema = z.object({
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  displayName: displayNameSchema,
  id: z.string().uuid(),
  slug: slugSchema,
  status: resourceStatusSchema,
  updatedAt: timestampSchema,
  version: z.number().int().positive(),
});

const applicationSchema = tenantSchema.extend({
  tenantId: z.string().uuid(),
});

const environmentSchema = applicationSchema.extend({
  applicationId: z.string().uuid(),
  environmentType: environmentTypeSchema,
  isProtected: z.boolean(),
});

const tenantMembershipSchema = z.object({
  actorId: z.string().uuid(),
  createdAt: timestampSchema,
  status: membershipStatusSchema,
  tenantId: z.string().uuid(),
  updatedAt: timestampSchema,
  version: z.number().int().positive(),
});

const tenantsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  tenants: z.array(tenantSchema),
});
const applicationsPageSchema = z.object({
  applications: z.array(applicationSchema),
  nextPageToken: z.string().nullish(),
});
const environmentsPageSchema = z.object({
  environments: z.array(environmentSchema),
  nextPageToken: z.string().nullish(),
});
const membershipsPageSchema = z.object({
  memberships: z.array(tenantMembershipSchema),
  nextPageToken: z.string().nullish(),
});

const idSchema = z.string().uuid();
const versionSchema = z.number().int().positive();
const pageSchema = z.object({
  includeArchived: z.boolean().default(false),
  pageSize: z.number().int().min(1).max(100).default(50),
  pageToken: z.string().default(""),
  query: z.string().trim().max(200).default(""),
});

export type TenantRecord = z.infer<typeof tenantSchema>;
export type ApplicationRecord = z.infer<typeof applicationSchema>;
export type EnvironmentRecord = z.infer<typeof environmentSchema>;
export type TenantMembershipRecord = z.infer<typeof tenantMembershipSchema>;
export type EnvironmentType = z.infer<typeof environmentTypeSchema>;
export type PageOptions = z.input<typeof pageSchema>;

export async function listTenants(options: PageOptions) {
  const page = pageSchema.parse(options);
  const response = await getAsterloomApiClient().api.v1.tenants.get({
    queryParameters: page,
  });
  return tenantsPageSchema.parse(requireResponse(response));
}

export async function createTenant(
  csrfToken: string,
  input: { displayName: string; slug: string },
) {
  const body = z
    .object({ displayName: displayNameSchema, slug: slugSchema })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants.post(body);
  return tenantSchema.parse(requireResponse(response));
}

export async function updateTenant(
  csrfToken: string,
  tenant: Pick<TenantRecord, "id" | "version">,
  displayName: string,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(idSchema.parse(tenant.id))
    .patch({
      displayName: displayNameSchema.parse(displayName),
      expectedVersion: versionSchema.parse(tenant.version),
    });
  return tenantSchema.parse(requireResponse(response));
}

export async function archiveTenant(csrfToken: string, tenant: TenantRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(idSchema.parse(tenant.id))
    .delete({ queryParameters: { expectedVersion: versionSchema.parse(tenant.version) } });
  return tenantSchema.parse(requireResponse(response));
}

export async function restoreTenant(csrfToken: string, tenant: TenantRecord) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .withTenantIdRestore(idSchema.parse(tenant.id))
    .post({ expectedVersion: versionSchema.parse(tenant.version) });
  return tenantSchema.parse(requireResponse(response));
}

export async function listApplications(tenantId: string, options: PageOptions) {
  const page = pageSchema.parse(options);
  const response = await getAsterloomApiClient().api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .applications.get({ queryParameters: page });
  return applicationsPageSchema.parse(requireResponse(response));
}

export async function createApplication(
  csrfToken: string,
  tenantId: string,
  input: { displayName: string; slug: string },
) {
  const body = z
    .object({ displayName: displayNameSchema, slug: slugSchema })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .applications.post(body);
  return applicationSchema.parse(requireResponse(response));
}

export async function updateApplication(
  csrfToken: string,
  application: ApplicationRecord,
  displayName: string,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(application.tenantId)
    .applications.byApplicationId(application.id)
    .patch({
      displayName: displayNameSchema.parse(displayName),
      expectedVersion: versionSchema.parse(application.version),
    });
  return applicationSchema.parse(requireResponse(response));
}

export async function archiveApplication(
  csrfToken: string,
  application: ApplicationRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(application.tenantId)
    .applications.byApplicationId(application.id)
    .delete({
      queryParameters: { expectedVersion: versionSchema.parse(application.version) },
    });
  return applicationSchema.parse(requireResponse(response));
}

export async function restoreApplication(
  csrfToken: string,
  application: ApplicationRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(application.tenantId)
    .applications.withApplicationIdRestore(application.id)
    .post({ expectedVersion: versionSchema.parse(application.version) });
  return applicationSchema.parse(requireResponse(response));
}

export async function listEnvironments(
  tenantId: string,
  applicationId: string,
  options: PageOptions,
) {
  const page = pageSchema.parse(options);
  const response = await getAsterloomApiClient().api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .applications.byApplicationId(idSchema.parse(applicationId))
    .environments.get({ queryParameters: page });
  return environmentsPageSchema.parse(requireResponse(response));
}

export async function createEnvironment(
  csrfToken: string,
  tenantId: string,
  applicationId: string,
  input: {
    displayName: string;
    environmentType: EnvironmentType;
    isProtected: boolean;
    slug: string;
  },
) {
  const body = z
    .object({
      displayName: displayNameSchema,
      environmentType: environmentTypeSchema,
      isProtected: z.boolean(),
      slug: slugSchema,
    })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .applications.byApplicationId(idSchema.parse(applicationId))
    .environments.post(body);
  return environmentSchema.parse(requireResponse(response));
}

export async function updateEnvironment(
  csrfToken: string,
  environment: EnvironmentRecord,
  input: {
    displayName: string;
    environmentType: EnvironmentType;
    isProtected: boolean;
  },
) {
  const body = z
    .object({
      displayName: displayNameSchema,
      environmentType: environmentTypeSchema,
      isProtected: z.boolean(),
    })
    .parse(input);
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(environment.tenantId)
    .applications.byApplicationId(environment.applicationId)
    .environments.byEnvironmentId(environment.id)
    .patch({ ...body, expectedVersion: versionSchema.parse(environment.version) });
  return environmentSchema.parse(requireResponse(response));
}

export async function archiveEnvironment(
  csrfToken: string,
  environment: EnvironmentRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(environment.tenantId)
    .applications.byApplicationId(environment.applicationId)
    .environments.byEnvironmentId(environment.id)
    .delete({
      queryParameters: { expectedVersion: versionSchema.parse(environment.version) },
    });
  return environmentSchema.parse(requireResponse(response));
}

export async function restoreEnvironment(
  csrfToken: string,
  environment: EnvironmentRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(environment.tenantId)
    .applications.byApplicationId(environment.applicationId)
    .environments.withEnvironmentIdRestore(environment.id)
    .post({ expectedVersion: versionSchema.parse(environment.version) });
  return environmentSchema.parse(requireResponse(response));
}

export async function listTenantMemberships(
  tenantId: string,
  options: Omit<PageOptions, "includeArchived" | "query"> & {
    includeRemoved?: boolean;
  },
) {
  const page = z
    .object({
      includeRemoved: z.boolean().default(false),
      pageSize: z.number().int().min(1).max(100).default(50),
      pageToken: z.string().default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .memberships.get({ queryParameters: page });
  return membershipsPageSchema.parse(requireResponse(response));
}

export async function setTenantMembership(
  csrfToken: string,
  tenantId: string,
  actorId: string,
  expectedVersion: number,
) {
  const parsedActorId = actorIdSchema.parse(actorId);
  const parsedVersion = z.number().int().nonnegative().parse(expectedVersion);
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(idSchema.parse(tenantId))
    .memberships.byActorId(parsedActorId)
    .put({ expectedVersion: parsedVersion });
  return tenantMembershipSchema.parse(requireResponse(response));
}

export async function removeTenantMembership(
  csrfToken: string,
  membership: TenantMembershipRecord,
) {
  const response = await getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(membership.tenantId)
    .memberships.byActorId(membership.actorId)
    .delete({
      queryParameters: { expectedVersion: versionSchema.parse(membership.version) },
    });
  return tenantMembershipSchema.parse(requireResponse(response));
}

export function platformErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted values are invalid.";
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

  return "The operation could not be completed.";
}

export function isVersionConflict(error: unknown): boolean {
  return platformErrorMessage(error).includes("changed since it was loaded");
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The platform API returned an empty response.");
  }
  return response;
}
