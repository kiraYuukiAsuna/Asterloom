import { z } from "zod";

import {
  CollectorHealthStatusObject,
  OtlpProtocolObject,
  TelemetryResourceStatusObject,
  TelemetrySignalTypeObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const optionalTextSchema = z.string().nullish().transform((value) => value ?? "");
const optionalTimestampSchema = timestampSchema.nullish().transform((value) => value ?? null);
const sourceKeyPattern = /^[a-z][a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const traceIdPattern = /^[0-9a-f]{32}$/;

export const telemetryScopeSchema = z.object({
  applicationId: idSchema,
  environmentId: idSchema,
  tenantId: idSchema,
});
export const telemetrySourceKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(sourceKeyPattern, "Use 2–100 lowercase letters, numbers, periods, underscores, or hyphens.");
export const telemetryDisplayNameSchema = z.string().trim().min(1).max(200);
export const telemetryDescriptionSchema = z.string().trim().max(2_000);
export const telemetryServiceNameSchema = z.string().trim().min(1).max(200);
export const telemetryAttributesSchema = z
  .string()
  .trim()
  .min(2)
  .max(8_192)
  .refine((value) => {
    try {
      const parsed: unknown = JSON.parse(value);
      return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed);
    } catch {
      return false;
    }
  }, "Enter a valid JSON object.");
export const telemetryUrlSchema = z.string().trim().url().max(2_048).refine(
  (value) => new URL(value).protocol === "http:" || new URL(value).protocol === "https:",
  "Use an HTTP(S) URL.",
);
export const telemetryOptionalUrlSchema = z.union([z.literal(""), telemetryUrlSchema]);

const statusSchema = z.enum([
  TelemetryResourceStatusObject.TELEMETRY_RESOURCE_STATUS_ACTIVE,
  TelemetryResourceStatusObject.TELEMETRY_RESOURCE_STATUS_ARCHIVED,
]);
const protocolSchema = z.enum([
  OtlpProtocolObject.OTLP_PROTOCOL_GRPC,
  OtlpProtocolObject.OTLP_PROTOCOL_HTTP_PROTOBUF,
]);
export const telemetrySignalTypeSchema = z.enum([
  TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
  TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_METRIC,
  TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_LOG,
]);
const scopeResponseSchema = telemetryScopeSchema;
const sourceRecordSchema = z.object({
  archivedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  description: optionalTextSchema,
  displayName: telemetryDisplayNameSchema,
  id: idSchema,
  key: telemetrySourceKeySchema,
  resourceAttributesJson: z.string().min(2),
  scope: scopeResponseSchema,
  serviceName: telemetryServiceNameSchema,
  status: statusSchema,
  updatedAt: timestampSchema,
  version: z.number().int().positive().safe(),
});
const settingsSchema = z.object({
  diagnosticsBaseUrl: optionalTextSchema,
  exporterEndpoint: telemetryUrlSchema,
  exporterProtocol: protocolSchema,
  logsEnabled: z.boolean(),
  metricsEnabled: z.boolean(),
  samplingRatio: z.number().min(0).max(1),
  scope: scopeResponseSchema,
  tracesEnabled: z.boolean(),
  updatedAt: timestampSchema,
  version: z.number().int().nonnegative().safe(),
});
const sourcePageSchema = z.object({
  nextPageToken: optionalTextSchema,
  sources: z.array(sourceRecordSchema).nullish().transform((value) => value ?? []),
});
const collectorHealthSchema = z.object({
  checkedAt: timestampSchema,
  endpoint: telemetryUrlSchema,
  latencyMilliseconds: z.number().int().nonnegative().safe(),
  message: z.string(),
  status: z.enum([
    CollectorHealthStatusObject.COLLECTOR_HEALTH_STATUS_HEALTHY,
    CollectorHealthStatusObject.COLLECTOR_HEALTH_STATUS_DEGRADED,
    CollectorHealthStatusObject.COLLECTOR_HEALTH_STATUS_UNAVAILABLE,
  ]),
});
const telemetryErrorSchema = z.object({
  exceptionType: z.string(),
  grpcMethod: z.string(),
  id: idSchema,
  message: z.string(),
  occurredAt: timestampSchema,
  requestId: optionalTextSchema,
  scope: scopeResponseSchema,
  serviceName: z.string(),
  spanId: optionalTextSchema,
  traceId: optionalTextSchema,
});
const errorPageSchema = z.object({
  errors: z.array(telemetryErrorSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const telemetryRecordSchema = z.object({
  attributesJson: z.string(),
  category: z.string(),
  createdAt: timestampSchema,
  durationMilliseconds: z.number().nonnegative().nullish().transform((value) => value ?? null),
  id: idSchema,
  name: z.string(),
  observedAt: timestampSchema,
  payloadJson: z.string(),
  scope: scopeResponseSchema,
  serviceName: z.string(),
  signalType: telemetrySignalTypeSchema,
  spanId: optionalTextSchema,
  traceId: optionalTextSchema,
  value: z.string(),
});
const telemetryRecordPageSchema = z.object({
  nextPageToken: optionalTextSchema,
  records: z.array(telemetryRecordSchema).nullish().transform((value) => value ?? []),
});
const diagnosticLinkSchema = z.object({
  fromAt: timestampSchema,
  toAt: timestampSchema,
  traceId: z.string().regex(traceIdPattern),
  url: telemetryUrlSchema,
});

export type TelemetryScope = z.infer<typeof telemetryScopeSchema>;
export type TelemetrySourceRecord = z.infer<typeof sourceRecordSchema>;
export type TelemetrySettingsRecord = z.infer<typeof settingsSchema>;
export type TelemetryCollectorHealthRecord = z.infer<typeof collectorHealthSchema>;
export type TelemetryErrorRecord = z.infer<typeof telemetryErrorSchema>;
export type TelemetryRecord = z.infer<typeof telemetryRecordSchema>;
export type TelemetrySignalType = z.infer<typeof telemetrySignalTypeSchema>;
export type TelemetryDiagnosticLinkRecord = z.infer<typeof diagnosticLinkSchema>;

export async function listTelemetrySources(
  scope: TelemetryScope,
  options: { includeArchived?: boolean; pageSize?: number; pageToken?: string; query?: string },
) {
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope)).sources.get({
    queryParameters: {
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
      pageSize: z.number().int().min(1).max(100).default(50).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      query: z.string().trim().max(200).default("").parse(options.query),
    },
  });
  return sourcePageSchema.parse(requireResponse(response));
}

export async function getTelemetrySource(scope: TelemetryScope, sourceId: string) {
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope))
    .sources.bySourceId(idSchema.parse(sourceId))
    .get();
  return sourceRecordSchema.parse(requireResponse(response));
}

export async function createTelemetrySource(
  csrfToken: string,
  scope: TelemetryScope,
  input: {
    description: string;
    displayName: string;
    key: string;
    resourceAttributesJson: string;
    serviceName: string;
  },
) {
  const parsed = z.object({
    description: telemetryDescriptionSchema,
    displayName: telemetryDisplayNameSchema,
    key: telemetrySourceKeySchema,
    resourceAttributesJson: telemetryAttributesSchema,
    serviceName: telemetryServiceNameSchema,
  }).parse(input);
  const response = await telemetryBuilder(scope, csrfToken).sources.post(parsed);
  return sourceRecordSchema.parse(requireResponse(response));
}

export async function updateTelemetrySource(
  csrfToken: string,
  source: TelemetrySourceRecord,
  input: {
    description: string;
    displayName: string;
    resourceAttributesJson: string;
    serviceName: string;
  },
) {
  const current = sourceRecordSchema.parse(source);
  const parsed = z.object({
    description: telemetryDescriptionSchema,
    displayName: telemetryDisplayNameSchema,
    resourceAttributesJson: telemetryAttributesSchema,
    serviceName: telemetryServiceNameSchema,
  }).parse(input);
  const response = await telemetryBuilder(current.scope, csrfToken)
    .sources.bySourceId(current.id)
    .patch({ ...parsed, expectedVersion: current.version });
  return sourceRecordSchema.parse(requireResponse(response));
}

export async function archiveTelemetrySource(
  csrfToken: string,
  source: TelemetrySourceRecord,
) {
  const current = sourceRecordSchema.parse(source);
  const response = await telemetryBuilder(current.scope, csrfToken)
    .sources.bySourceId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return sourceRecordSchema.parse(requireResponse(response));
}

export async function restoreTelemetrySource(
  csrfToken: string,
  source: TelemetrySourceRecord,
) {
  const current = sourceRecordSchema.parse(source);
  const response = await telemetryBuilder(current.scope, csrfToken)
    .sources.withSourceIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return sourceRecordSchema.parse(requireResponse(response));
}

export async function getTelemetrySettings(scope: TelemetryScope) {
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope)).settings.get();
  return settingsSchema.parse(requireResponse(response));
}

export async function updateTelemetrySettings(
  csrfToken: string,
  settings: TelemetrySettingsRecord,
  input: {
    diagnosticsBaseUrl: string;
    exporterEndpoint: string;
    exporterProtocol: z.infer<typeof protocolSchema>;
    logsEnabled: boolean;
    metricsEnabled: boolean;
    samplingRatio: number;
    tracesEnabled: boolean;
  },
) {
  const current = settingsSchema.parse(settings);
  const parsed = z.object({
    diagnosticsBaseUrl: telemetryOptionalUrlSchema,
    exporterEndpoint: telemetryUrlSchema,
    exporterProtocol: protocolSchema,
    logsEnabled: z.boolean(),
    metricsEnabled: z.boolean(),
    samplingRatio: z.number().min(0).max(1),
    tracesEnabled: z.boolean(),
  }).parse(input);
  const response = await telemetryBuilder(current.scope, csrfToken).settings.put({
    ...parsed,
    expectedVersion: current.version,
  });
  return settingsSchema.parse(requireResponse(response));
}

export async function getTelemetryCollectorHealth(scope: TelemetryScope) {
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope)).collector.health.get();
  return collectorHealthSchema.parse(requireResponse(response));
}

export async function listTelemetryErrors(
  scope: TelemetryScope,
  options: { pageSize?: number; pageToken?: string; serviceName?: string; traceId?: string },
) {
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope)).errors.get({
    queryParameters: {
      pageSize: z.number().int().min(1).max(100).default(50).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      serviceName: z.string().trim().max(200).default("").parse(options.serviceName),
      traceId: options.traceId
        ? z.string().trim().toLowerCase().regex(traceIdPattern).parse(options.traceId)
        : "",
    },
  });
  return errorPageSchema.parse(requireResponse(response));
}

export async function listTelemetryRecords(
  scope: TelemetryScope,
  options: {
    fromAt?: string;
    pageSize?: number;
    pageToken?: string;
    query?: string;
    serviceName?: string;
    signalType: TelemetrySignalType;
    toAt?: string;
    traceId?: string;
  },
) {
  const parsed = z.object({
    fromAt: z.string().datetime().optional(),
    pageSize: z.number().int().min(1).max(100).default(50),
    pageToken: z.string().max(2_048).default(""),
    query: z.string().trim().max(200).default(""),
    serviceName: z.string().trim().max(200).default(""),
    signalType: telemetrySignalTypeSchema,
    toAt: z.string().datetime().optional(),
    traceId: z.union([z.literal(""), z.string().trim().toLowerCase().regex(traceIdPattern)]).default(""),
  }).parse(options);
  const response = await telemetryBuilder(telemetryScopeSchema.parse(scope)).records.get({
    queryParameters: parsed,
  });
  return telemetryRecordPageSchema.parse(requireResponse(response));
}

export async function getTelemetryDiagnosticLink(
  csrfToken: string,
  scope: TelemetryScope,
  input: { fromAt?: string; toAt?: string; traceId: string },
) {
  const parsed = z.object({
    fromAt: z.string().datetime().optional(),
    toAt: z.string().datetime().optional(),
    traceId: z.string().trim().toLowerCase().regex(traceIdPattern),
  }).parse(input);
  const response = await environmentBuilder(scope, csrfToken).telemetryDiagnosticLink.post(parsed);
  return diagnosticLinkSchema.parse(requireResponse(response));
}

export function telemetryErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted telemetry values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The telemetry operation could not be completed.";
}

function environmentBuilder(scope: TelemetryScope, csrfToken?: string) {
  const parsed = telemetryScopeSchema.parse(scope);
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(parsed.tenantId)
    .applications.byApplicationId(parsed.applicationId)
    .environments.byEnvironmentId(parsed.environmentId);
}

function telemetryBuilder(scope: TelemetryScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).telemetry;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Telemetry API returned an empty response.");
  return response;
}
