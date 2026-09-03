import { z } from "zod";

import {
  TargetingConditionReasonObject,
  TargetingMatchModeObject,
  TargetingOperatorObject,
  TargetingResourceStatusObject,
  TargetingValueKindObject,
  type BucketPreview,
  type EvaluationContext,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const versionSchema = z.number().int().positive();
const targetingKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const attributePattern = /^[a-z][a-zA-Z0-9_.-]{0,63}$/;

const valueKindSchema = z.enum([
  TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT,
  TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH,
  TargetingValueKindObject.TARGETING_VALUE_KIND_NUMERIC,
]);
const matchModeSchema = z.enum([
  TargetingMatchModeObject.TARGETING_MATCH_MODE_ALL,
  TargetingMatchModeObject.TARGETING_MATCH_MODE_ANY,
]);
const operatorSchema = z.enum([
  TargetingOperatorObject.TARGETING_OPERATOR_EQUALS,
  TargetingOperatorObject.TARGETING_OPERATOR_NOT_EQUALS,
  TargetingOperatorObject.TARGETING_OPERATOR_ONE_OF,
  TargetingOperatorObject.TARGETING_OPERATOR_NOT_ONE_OF,
  TargetingOperatorObject.TARGETING_OPERATOR_CONTAINS,
  TargetingOperatorObject.TARGETING_OPERATOR_STARTS_WITH,
  TargetingOperatorObject.TARGETING_OPERATOR_ENDS_WITH,
  TargetingOperatorObject.TARGETING_OPERATOR_GREATER_THAN,
  TargetingOperatorObject.TARGETING_OPERATOR_GREATER_THAN_OR_EQUAL,
  TargetingOperatorObject.TARGETING_OPERATOR_LESS_THAN,
  TargetingOperatorObject.TARGETING_OPERATOR_LESS_THAN_OR_EQUAL,
  TargetingOperatorObject.TARGETING_OPERATOR_EXISTS,
  TargetingOperatorObject.TARGETING_OPERATOR_NOT_EXISTS,
  TargetingOperatorObject.TARGETING_OPERATOR_SEMANTIC_VERSION_EQUALS,
  TargetingOperatorObject.TARGETING_OPERATOR_SEMANTIC_VERSION_GREATER_THAN,
  TargetingOperatorObject.TARGETING_OPERATOR_SEMANTIC_VERSION_LESS_THAN,
]);
const conditionReasonSchema = z.enum([
  TargetingConditionReasonObject.TARGETING_CONDITION_REASON_MATCHED,
  TargetingConditionReasonObject.TARGETING_CONDITION_REASON_NOT_MATCHED,
  TargetingConditionReasonObject.TARGETING_CONDITION_REASON_MISSING_ATTRIBUTE,
  TargetingConditionReasonObject.TARGETING_CONDITION_REASON_TYPE_MISMATCH,
  TargetingConditionReasonObject.TARGETING_CONDITION_REASON_INVALID_ATTRIBUTE_VALUE,
]);
const statusSchema = z.enum([
  TargetingResourceStatusObject.TARGETING_RESOURCE_STATUS_ACTIVE,
  TargetingResourceStatusObject.TARGETING_RESOURCE_STATUS_ARCHIVED,
]);

export const segmentKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(
    targetingKeyPattern,
    "Use 1–100 lowercase letters, numbers, periods, underscores, or hyphens.",
  );
export const segmentNameSchema = z.string().trim().min(1).max(200);
export const segmentDescriptionSchema = z.string().trim().max(1000);
export const targetingAttributeNameSchema = z
  .string()
  .trim()
  .regex(attributePattern, "Enter a valid built-in or custom attribute name.");

export const targetingValueSchema = z.union([
  z.object({ text: z.string().max(1000) }),
  z.object({ truth: z.boolean() }),
  z.object({ numeric: z.number().finite() }),
]);
const targetingConditionSchema = z.object({
  attribute: targetingAttributeNameSchema,
  caseSensitive: z.boolean().default(false),
  id: z.string().trim().min(1).max(100),
  operator: operatorSchema,
  valueKind: valueKindSchema,
  values: z.array(targetingValueSchema).max(50),
});
export const targetingRuleSchema = z.object({
  conditions: z.array(targetingConditionSchema).min(1).max(50),
  matchMode: matchModeSchema,
});
const segmentSchema = z.object({
  applicationId: idSchema,
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: z.string(),
  displayName: segmentNameSchema,
  environmentId: idSchema,
  id: idSchema,
  key: segmentKeySchema,
  rule: targetingRuleSchema,
  status: statusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  version: versionSchema,
});
const segmentsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  segments: z.array(segmentSchema),
});
const attributeDefinitionSchema = z.object({
  builtIn: z.boolean(),
  displayName: z.string().min(1),
  key: z.string().min(1),
  required: z.boolean(),
  valueKind: valueKindSchema,
});
const operatorDefinitionSchema = z.object({
  displayName: z.string().min(1),
  maximumValues: z.number().int().min(0).max(50),
  minimumValues: z.number().int().min(0).max(50),
  operator: operatorSchema,
  supportedValueKinds: z.array(valueKindSchema).min(1),
});
const catalogSchema = z.object({
  attributes: z.array(attributeDefinitionSchema),
  bucketCount: z.number().int().positive(),
  bucketingVersion: z.string().min(1),
  maximumConditions: z.number().int().positive(),
  maximumCustomAttributes: z.number().int().positive(),
  operators: z.array(operatorDefinitionSchema),
});
const conditionTraceSchema = z.object({
  conditionId: z.string().min(1),
  matched: z.boolean(),
  reason: conditionReasonSchema,
});
const simulationResultSchema = z.object({
  bucket: z.number().int().min(0),
  bucketEvaluated: z.boolean(),
  bucketingVersion: z.string().min(1),
  bucketNamespace: z.string(),
  conditionTraces: z.array(conditionTraceSchema),
  matched: z.boolean(),
  reason: z.string().min(1),
  segmentId: idSchema,
  segmentKey: segmentKeySchema,
  segmentVersion: versionSchema,
  selectedVariant: z.string(),
});
const scopeSchema = z.object({
  applicationId: idSchema,
  environmentId: idSchema,
  tenantId: idSchema,
});
const pageSchema = z.object({
  includeArchived: z.boolean().default(false),
  pageSize: z.number().int().min(1).max(100).default(50),
  pageToken: z.string().default(""),
  query: z.string().trim().max(200).default(""),
});
const segmentInputSchema = z.object({
  description: segmentDescriptionSchema,
  displayName: segmentNameSchema,
  rule: targetingRuleSchema,
});
const contextSchema = z.object({
  attributes: z
    .array(
      z.object({
        key: targetingAttributeNameSchema,
        value: targetingValueSchema,
      }),
    )
    .max(64)
    .default([]),
  clientVersion: z.string().trim().max(100).default(""),
  language: z.string().trim().max(100).default(""),
  platform: z.string().trim().max(100).default(""),
  region: z.string().trim().max(100).default(""),
  targetingKey: z.string().trim().min(1).max(512),
  userId: z.string().trim().max(200).default(""),
});
const bucketPreviewSchema = z.object({
  allocations: z
    .array(
      z.object({
        end: z.number().int().min(1).max(100_000),
        start: z.number().int().min(0).max(99_999),
        variant: z.string().trim().min(1).max(100),
      }),
    )
    .max(100),
  resourceKey: segmentKeySchema,
  resourceType: segmentKeySchema,
  salt: z.string().max(500),
});

export type TargetingScope = z.infer<typeof scopeSchema>;
export type TargetingValueInput = z.infer<typeof targetingValueSchema>;
export type TargetingConditionInput = z.infer<typeof targetingConditionSchema>;
export type TargetingRuleInput = z.infer<typeof targetingRuleSchema>;
export type TargetingSegmentRecord = z.infer<typeof segmentSchema>;
export type TargetingCatalog = z.infer<typeof catalogSchema>;
export type TargetingSimulationResult = z.infer<typeof simulationResultSchema>;
export type TargetingContextInput = z.input<typeof contextSchema>;
export type TargetingBucketPreviewInput = z.input<typeof bucketPreviewSchema>;

export async function listTargetingAttributes() {
  const response = await getAsterloomApiClient().api.v1.targeting.attributes.get();
  return catalogSchema.parse(requireResponse(response));
}

export async function listSegments(
  scope: TargetingScope,
  options: z.input<typeof pageSchema>,
) {
  const parsedScope = scopeSchema.parse(scope);
  const page = pageSchema.parse(options);
  const response = await environmentBuilder(parsedScope).targeting.segments.get({
    queryParameters: page,
  });
  return segmentsPageSchema.parse(requireResponse(response));
}

export async function getSegment(scope: TargetingScope, segmentId: string) {
  const parsedScope = scopeSchema.parse(scope);
  const response = await environmentBuilder(parsedScope).targeting.segments
    .bySegmentId(idSchema.parse(segmentId))
    .get();
  return segmentSchema.parse(requireResponse(response));
}

export async function createSegment(
  csrfToken: string,
  scope: TargetingScope,
  input: {
    description: string;
    displayName: string;
    key: string;
    rule: TargetingRuleInput;
  },
) {
  const parsedScope = scopeSchema.parse(scope);
  const body = segmentInputSchema
    .extend({ key: segmentKeySchema })
    .parse(input);
  const response = await environmentBuilder(parsedScope, csrfToken).targeting.segments.post(
    body,
  );
  return segmentSchema.parse(requireResponse(response));
}

export async function updateSegment(
  csrfToken: string,
  segment: TargetingSegmentRecord,
  input: { description: string; displayName: string; rule: TargetingRuleInput },
) {
  const current = segmentSchema.parse(segment);
  const body = segmentInputSchema.parse(input);
  const response = await environmentBuilder(current, csrfToken).targeting.segments
    .bySegmentId(current.id)
    .patch({ ...body, expectedVersion: current.version });
  return segmentSchema.parse(requireResponse(response));
}

export async function archiveSegment(
  csrfToken: string,
  segment: TargetingSegmentRecord,
) {
  const current = segmentSchema.parse(segment);
  const response = await environmentBuilder(current, csrfToken).targeting.segments
    .bySegmentId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return segmentSchema.parse(requireResponse(response));
}

export async function restoreSegment(
  csrfToken: string,
  segment: TargetingSegmentRecord,
) {
  const current = segmentSchema.parse(segment);
  const response = await environmentBuilder(current, csrfToken).targeting.segments
    .withSegmentIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return segmentSchema.parse(requireResponse(response));
}

export async function simulateTargeting(
  csrfToken: string,
  scope: TargetingScope,
  segmentId: string,
  context: TargetingContextInput,
  bucketPreview?: TargetingBucketPreviewInput,
) {
  const parsedScope = scopeSchema.parse(scope);
  const parsedContext = contextSchema.parse(context);
  const parsedPreview = bucketPreview
    ? bucketPreviewSchema.parse(bucketPreview)
    : undefined;
  const response = await environmentBuilder(parsedScope, csrfToken).targetingSimulate.post({
    bucketPreview: parsedPreview as BucketPreview | undefined,
    context: parsedContext as EvaluationContext,
    segmentId: idSchema.parse(segmentId),
  });
  return simulationResultSchema.parse(requireResponse(response));
}

export function targetingErrorMessage(error: unknown): string {
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

  return "The targeting operation could not be completed.";
}

export function isTargetingVersionConflict(error: unknown): boolean {
  return targetingErrorMessage(error).includes("changed since it was loaded");
}

function environmentBuilder(scope: TargetingScope, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(scope.tenantId)
    .applications.byApplicationId(scope.applicationId)
    .environments.byEnvironmentId(scope.environmentId);
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The targeting API returned an empty response.");
  }
  return response;
}
