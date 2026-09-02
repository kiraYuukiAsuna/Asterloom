import { z } from "zod";

import {
  AnalyticsResourceStatusObject,
  AnalyticsWriteKeyStatusObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const positiveVersionSchema = z.number().int().positive().safe();
const eventKeyPattern = /^[a-z][a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const optionalTextSchema = z.string().nullish().transform((value) => value ?? "");
const optionalTimestampSchema = timestampSchema.nullish().transform((value) => value ?? null);

export const analyticsScopeSchema = z.object({
  applicationId: idSchema,
  environmentId: idSchema,
  tenantId: idSchema,
});
export const analyticsEventKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(eventKeyPattern, "Use 2–100 lowercase letters, numbers, periods, underscores, or hyphens.");
export const analyticsDisplayNameSchema = z.string().trim().min(1).max(200);
export const analyticsDescriptionSchema = z.string().trim().max(2_000);
export const analyticsRetentionSchema = z.number().int().min(1).max(3_650);
export const analyticsSchemaJsonSchema = z
  .string()
  .trim()
  .min(2)
  .max(100_000)
  .refine((value) => {
    try {
      return typeof JSON.parse(value) === "object";
    } catch {
      return false;
    }
  }, "Enter a valid JSON object schema.");

const scopeResponseSchema = analyticsScopeSchema;
const resourceStatusSchema = z.enum([
  AnalyticsResourceStatusObject.ANALYTICS_RESOURCE_STATUS_ACTIVE,
  AnalyticsResourceStatusObject.ANALYTICS_RESOURCE_STATUS_ARCHIVED,
]);
const writeKeyStatusSchema = z.enum([
  AnalyticsWriteKeyStatusObject.ANALYTICS_WRITE_KEY_STATUS_ACTIVE,
  AnalyticsWriteKeyStatusObject.ANALYTICS_WRITE_KEY_STATUS_REVOKED,
]);
const eventSchemaRecordSchema = z.object({
  archivedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  description: optionalTextSchema,
  displayName: analyticsDisplayNameSchema,
  id: idSchema,
  key: analyticsEventKeySchema,
  retentionDays: analyticsRetentionSchema,
  schemaJson: z.string().min(2),
  scope: scopeResponseSchema,
  status: resourceStatusSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});
const writeKeyRecordSchema = z.object({
  createdAt: timestampSchema,
  id: idSchema,
  lastUsedAt: optionalTimestampSchema,
  name: analyticsDisplayNameSchema,
  prefix: z.string().min(6).max(64),
  revokedAt: optionalTimestampSchema,
  scope: scopeResponseSchema,
  status: writeKeyStatusSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});
const writeKeyCredentialSchema = z.object({
  secret: z.string().min(32),
  writeKey: writeKeyRecordSchema,
});
const analyticsEventRecordSchema = z.object({
  actorId: optionalTextSchema,
  anonymousId: optionalTextSchema,
  contextJson: z.string().min(2),
  eventId: z.string().min(1),
  eventName: analyticsEventKeySchema,
  id: idSchema,
  occurredAt: timestampSchema,
  propertiesJson: z.string().min(2),
  receivedAt: timestampSchema,
  schemaVersion: positiveVersionSchema,
  scope: scopeResponseSchema,
  sdkName: optionalTextSchema,
  sdkVersion: optionalTextSchema,
  sessionId: optionalTextSchema,
  writeKeyPrefix: z.string().min(1),
});
const aggregationBucketSchema = z.object({
  eventCount: z.number().int().nonnegative().safe(),
  eventName: analyticsEventKeySchema,
  periodStart: timestampSchema,
  uniqueActors: z.number().int().nonnegative().safe(),
});
const schemaPageSchema = z.object({
  eventSchemas: z.array(eventSchemaRecordSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const eventPageSchema = z.object({
  events: z.array(analyticsEventRecordSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const writeKeyPageSchema = z.object({
  writeKeys: z.array(writeKeyRecordSchema).nullish().transform((value) => value ?? []),
});
const queryResponseSchema = z.object({
  buckets: z.array(aggregationBucketSchema).nullish().transform((value) => value ?? []),
});
const exportResponseSchema = z.object({
  content: z.string(),
  contentType: z.string().min(1),
  exportedRows: z.number().int().nonnegative(),
  fileName: z.string().min(1),
});

export type AnalyticsScope = z.infer<typeof analyticsScopeSchema>;
export type AnalyticsEventSchemaRecord = z.infer<typeof eventSchemaRecordSchema>;
export type AnalyticsWriteKeyRecord = z.infer<typeof writeKeyRecordSchema>;
export type AnalyticsWriteKeyCredential = z.infer<typeof writeKeyCredentialSchema>;
export type AnalyticsEventRecord = z.infer<typeof analyticsEventRecordSchema>;
export type AnalyticsAggregationBucket = z.infer<typeof aggregationBucketSchema>;

export async function listAnalyticsSchemas(
  scope: AnalyticsScope,
  options: { includeArchived?: boolean; pageSize?: number; pageToken?: string; query?: string },
) {
  const response = await insightsBuilder(analyticsScopeSchema.parse(scope)).schemas.get({
    queryParameters: {
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
      pageSize: z.number().int().min(1).max(100).default(50).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      query: z.string().trim().max(200).default("").parse(options.query),
    },
  });
  return schemaPageSchema.parse(requireResponse(response));
}

export async function getAnalyticsSchema(scope: AnalyticsScope, schemaId: string) {
  const response = await insightsBuilder(analyticsScopeSchema.parse(scope))
    .schemas.byEventSchemaId(idSchema.parse(schemaId))
    .get();
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function createAnalyticsSchema(
  csrfToken: string,
  scope: AnalyticsScope,
  input: {
    description: string;
    displayName: string;
    key: string;
    retentionDays: number;
    schemaJson: string;
  },
) {
  const parsed = z
    .object({
      description: analyticsDescriptionSchema,
      displayName: analyticsDisplayNameSchema,
      key: analyticsEventKeySchema,
      retentionDays: analyticsRetentionSchema,
      schemaJson: analyticsSchemaJsonSchema,
    })
    .parse(input);
  const response = await insightsBuilder(scope, csrfToken).schemas.post(parsed);
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function updateAnalyticsSchema(
  csrfToken: string,
  schema: AnalyticsEventSchemaRecord,
  input: { description: string; displayName: string; schemaJson: string },
) {
  const current = eventSchemaRecordSchema.parse(schema);
  const parsed = z
    .object({
      description: analyticsDescriptionSchema,
      displayName: analyticsDisplayNameSchema,
      schemaJson: analyticsSchemaJsonSchema,
    })
    .parse(input);
  const response = await insightsBuilder(current.scope, csrfToken)
    .schemas.byEventSchemaId(current.id)
    .patch({ ...parsed, expectedVersion: current.version });
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function archiveAnalyticsSchema(
  csrfToken: string,
  schema: AnalyticsEventSchemaRecord,
) {
  const current = eventSchemaRecordSchema.parse(schema);
  const response = await insightsBuilder(current.scope, csrfToken)
    .schemas.byEventSchemaId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function restoreAnalyticsSchema(
  csrfToken: string,
  schema: AnalyticsEventSchemaRecord,
) {
  const current = eventSchemaRecordSchema.parse(schema);
  const response = await insightsBuilder(current.scope, csrfToken)
    .schemas.withEventSchemaIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function updateAnalyticsRetention(
  csrfToken: string,
  schema: AnalyticsEventSchemaRecord,
  retentionDays: number,
) {
  const current = eventSchemaRecordSchema.parse(schema);
  const response = await insightsBuilder(current.scope, csrfToken)
    .schemas.byEventSchemaId(current.id)
    .retention.patch({
      expectedVersion: current.version,
      retentionDays: analyticsRetentionSchema.parse(retentionDays),
    });
  return eventSchemaRecordSchema.parse(requireResponse(response));
}

export async function listAnalyticsWriteKeys(
  scope: AnalyticsScope,
  includeRevoked = false,
) {
  const response = await insightsBuilder(analyticsScopeSchema.parse(scope)).writeKeys.get({
    queryParameters: { includeRevoked },
  });
  return writeKeyPageSchema.parse(requireResponse(response));
}

export async function createAnalyticsWriteKey(
  csrfToken: string,
  scope: AnalyticsScope,
  name: string,
) {
  const response = await insightsBuilder(scope, csrfToken).writeKeys.post({
    name: analyticsDisplayNameSchema.parse(name),
  });
  return writeKeyCredentialSchema.parse(requireResponse(response));
}

export async function rotateAnalyticsWriteKey(
  csrfToken: string,
  writeKey: AnalyticsWriteKeyRecord,
) {
  const current = writeKeyRecordSchema.parse(writeKey);
  const response = await insightsBuilder(current.scope, csrfToken)
    .writeKeys.withWriteKeyIdRotate(current.id)
    .post({ expectedVersion: current.version });
  return writeKeyCredentialSchema.parse(requireResponse(response));
}

export async function revokeAnalyticsWriteKey(
  csrfToken: string,
  writeKey: AnalyticsWriteKeyRecord,
) {
  const current = writeKeyRecordSchema.parse(writeKey);
  const response = await insightsBuilder(current.scope, csrfToken)
    .writeKeys.withWriteKeyIdRevoke(current.id)
    .post({ expectedVersion: current.version });
  return writeKeyRecordSchema.parse(requireResponse(response));
}

export async function listAnalyticsEvents(
  scope: AnalyticsScope,
  options: {
    actorId?: string;
    eventId?: string;
    eventName?: string;
    fromAt?: string;
    pageSize?: number;
    pageToken?: string;
    toAt?: string;
  },
) {
  const response = await insightsBuilder(analyticsScopeSchema.parse(scope)).events.get({
    queryParameters: {
      actorId: z.string().trim().max(200).default("").parse(options.actorId),
      eventId: z.string().trim().max(128).default("").parse(options.eventId),
      eventName: z.string().trim().max(100).default("").parse(options.eventName),
      fromAt: options.fromAt ? z.string().datetime().parse(options.fromAt) : undefined,
      pageSize: z.number().int().min(1).max(100).default(50).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      toAt: options.toAt ? z.string().datetime().parse(options.toAt) : undefined,
    },
  });
  return eventPageSchema.parse(requireResponse(response));
}

export async function getAnalyticsEvent(scope: AnalyticsScope, eventId: string) {
  const response = await insightsBuilder(analyticsScopeSchema.parse(scope))
    .events.byAnalyticsEventId(idSchema.parse(eventId))
    .get();
  return analyticsEventRecordSchema.parse(requireResponse(response));
}

export async function queryAnalytics(
  csrfToken: string,
  scope: AnalyticsScope,
  input: { eventNames: string[]; fromAt: string; interval: "hour" | "day" | "week"; toAt: string },
) {
  const parsed = z
    .object({
      eventNames: z.array(analyticsEventKeySchema).max(20),
      fromAt: z.string().datetime(),
      interval: z.enum(["hour", "day", "week"]),
      toAt: z.string().datetime(),
    })
    .parse(input);
  const response = await environmentBuilder(scope, csrfToken).insightsQuery.post(parsed);
  return queryResponseSchema.parse(requireResponse(response));
}

export async function exportAnalyticsEvents(
  csrfToken: string,
  scope: AnalyticsScope,
  input: {
    actorId?: string;
    eventName?: string;
    fromAt?: string;
    maximumRows?: number;
    toAt?: string;
  },
) {
  const parsed = z
    .object({
      actorId: z.string().trim().max(200).default(""),
      eventName: z.string().trim().max(100).default(""),
      fromAt: z.string().datetime().optional(),
      maximumRows: z.number().int().min(1).max(10_000).default(1_000),
      toAt: z.string().datetime().optional(),
    })
    .parse(input);
  const response = await insightsBuilder(scope, csrfToken).eventsExport.post(parsed);
  return exportResponseSchema.parse(requireResponse(response));
}

export function analyticsErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted analytics values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The analytics operation could not be completed.";
}

function environmentBuilder(scope: AnalyticsScope, csrfToken?: string) {
  const parsed = analyticsScopeSchema.parse(scope);
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(parsed.tenantId)
    .applications.byApplicationId(parsed.applicationId)
    .environments.byEnvironmentId(parsed.environmentId);
}

function insightsBuilder(scope: AnalyticsScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).insights;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Analytics API returned an empty response.");
  return response;
}
