import { z } from "zod";

import {
  FeatureEvaluationReasonObject,
  FeatureResourceStatusObject,
  FeatureValidationSeverityObject,
  FeatureValueKindObject,
  type EvaluationContext,
  type FeatureDefinition,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const positiveVersionSchema = z.number().int().positive();
const stableKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const ruleIdPattern = /^[a-zA-Z0-9](?:[a-zA-Z0-9._-]{0,98}[a-zA-Z0-9])?$/;
const attributePattern = /^[a-z][a-zA-Z0-9_.-]{0,63}$/;

export const featureValueKindSchema = z.enum([
  FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
  FeatureValueKindObject.FEATURE_VALUE_KIND_STRING,
  FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER,
  FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE,
  FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT,
]);
const resourceStatusSchema = z.enum([
  FeatureResourceStatusObject.FEATURE_RESOURCE_STATUS_ACTIVE,
  FeatureResourceStatusObject.FEATURE_RESOURCE_STATUS_ARCHIVED,
]);
const validationSeveritySchema = z.enum([
  FeatureValidationSeverityObject.FEATURE_VALIDATION_SEVERITY_ERROR,
  FeatureValidationSeverityObject.FEATURE_VALIDATION_SEVERITY_WARNING,
]);
const evaluationReasonSchema = z.enum([
  FeatureEvaluationReasonObject.FEATURE_EVALUATION_REASON_DISABLED,
  FeatureEvaluationReasonObject.FEATURE_EVALUATION_REASON_TARGETING_MATCH,
  FeatureEvaluationReasonObject.FEATURE_EVALUATION_REASON_SPLIT,
  FeatureEvaluationReasonObject.FEATURE_EVALUATION_REASON_DEFAULT,
  FeatureEvaluationReasonObject.FEATURE_EVALUATION_REASON_PREREQUISITE_FAILED,
]);

export const featureKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(
    stableKeyPattern,
    "Use 1–100 lowercase letters, numbers, periods, underscores, or hyphens.",
  );
export const featureDisplayNameSchema = z.string().trim().min(1).max(200);
export const featureDescriptionSchema = z.string().trim().max(1000);
export const featureVariantKeySchema = featureKeySchema;

export const featureValueSchema = z.union([
  z.object({ booleanValue: z.boolean() }),
  z.object({ stringValue: z.string().max(10_000) }),
  z.object({ integerValue: z.number().int().safe() }),
  z.object({ doubleValue: z.number().finite() }),
  z.object({
    objectJson: z.string().max(100_000).refine(isJsonObject, "Enter a JSON object."),
  }),
]);

const variantSchema = z.object({
  displayName: z.string().trim().min(1).max(200),
  key: featureVariantKeySchema,
  value: featureValueSchema,
});
const prerequisiteSchema = z.object({
  expectedVariantKey: featureVariantKeySchema,
  flagKey: featureKeySchema,
});
const targetingRuleSchema = z.object({
  id: z.string().trim().regex(ruleIdPattern, "Enter a stable rule ID."),
  segmentId: idSchema,
  variantKey: featureVariantKeySchema,
});
const allocationSchema = z
  .object({
    end: z.number().int().min(1).max(100_000),
    start: z.number().int().min(0).max(99_999),
    variantKey: featureVariantKeySchema,
  })
  .refine((value) => value.start < value.end, {
    message: "Allocation start must be lower than end.",
  });

export const featureDefinitionSchema = z
  .object({
    allocations: z.array(allocationSchema).max(100),
    bucketingSalt: z
      .string()
      .min(1)
      .max(500)
      .refine((value) => !Array.from(value).some(isControlCharacter), {
        message: "Bucketing salt cannot contain control characters.",
      }),
    defaultVariantKey: featureVariantKeySchema,
    enabled: z.boolean(),
    prerequisites: z.array(prerequisiteSchema).max(20),
    targetingRules: z.array(targetingRuleSchema).max(50),
    variants: z.array(variantSchema).min(1).max(20),
  })
  .superRefine((definition, context) => {
    const variantKeys = new Set<string>();
    definition.variants.forEach((variant, index) => {
      if (variantKeys.has(variant.key)) {
        context.addIssue({
          code: "custom",
          message: `Variant key '${variant.key}' is duplicated.`,
          path: ["variants", index, "key"],
        });
      }
      variantKeys.add(variant.key);
    });
    if (!variantKeys.has(definition.defaultVariantKey)) {
      context.addIssue({
        code: "custom",
        message: "Default variant must reference an existing variant.",
        path: ["defaultVariantKey"],
      });
    }
    definition.targetingRules.forEach((rule, index) => {
      if (!variantKeys.has(rule.variantKey)) {
        context.addIssue({
          code: "custom",
          message: "Targeting rule must reference an existing variant.",
          path: ["targetingRules", index, "variantKey"],
        });
      }
    });
    definition.allocations.forEach((allocation, index) => {
      if (!variantKeys.has(allocation.variantKey)) {
        context.addIssue({
          code: "custom",
          message: "Allocation must reference an existing variant.",
          path: ["allocations", index, "variantKey"],
        });
      }
      const overlap = definition.allocations.some(
        (candidate, candidateIndex) =>
          candidateIndex < index &&
          allocation.start < candidate.end &&
          allocation.end > candidate.start,
      );
      if (overlap) {
        context.addIssue({
          code: "custom",
          message: "Allocation ranges cannot overlap.",
          path: ["allocations", index],
        });
      }
    });
  });

const featureFlagSchema = z.object({
  applicationId: idSchema,
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: z.string(),
  displayName: featureDisplayNameSchema,
  draftDefinition: featureDefinitionSchema,
  draftRevision: positiveVersionSchema,
  environmentId: idSchema,
  id: idSchema,
  key: featureKeySchema,
  publishedAt: timestampSchema.nullish(),
  publishedDefinition: featureDefinitionSchema.nullish(),
  publishedRevision: z.number().int().nonnegative(),
  status: resourceStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  valueKind: featureValueKindSchema,
  version: positiveVersionSchema,
});
const flagsPageSchema = z.object({
  flags: z.array(featureFlagSchema),
  nextPageToken: z.string().nullish(),
});
const revisionSchema = z.object({
  definition: featureDefinitionSchema,
  flagId: idSchema,
  id: idSchema,
  publishedAt: timestampSchema,
  revision: positiveVersionSchema,
  sourceRevision: z.number().int().nonnegative(),
});
const revisionsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  revisions: z.array(revisionSchema),
});
const validationIssueSchema = z.object({
  code: z.string().min(1),
  message: z.string().min(1),
  path: z.string(),
  severity: validationSeveritySchema,
});
const validationResultSchema = z.object({
  definitionHash: z.string().min(1),
  issues: z.array(validationIssueSchema),
  valid: z.boolean(),
});
const evaluationSchema = z.object({
  bucket: z.number().int().min(0),
  bucketEvaluated: z.boolean(),
  bucketingVersion: z.string().min(1),
  flagId: idSchema,
  flagKey: featureKeySchema,
  reason: evaluationReasonSchema,
  revision: positiveVersionSchema,
  trace: z.array(z.string()),
  usedDraft: z.boolean(),
  value: featureValueSchema,
  variantKey: featureVariantKeySchema,
});
const scopeSchema = z.object({
  applicationId: idSchema,
  environmentId: idSchema,
  tenantId: idSchema,
});
const pageSchema = z.object({
  includeArchived: z.boolean().default(false),
  pageSize: z.number().int().min(1).max(100).default(25),
  pageToken: z.string().default(""),
  query: z.string().trim().max(200).default(""),
});
const contextValueSchema = z.union([
  z.object({ text: z.string().max(1000) }),
  z.object({ truth: z.boolean() }),
  z.object({ numeric: z.number().finite() }),
]);
const contextSchema = z.object({
  attributes: z
    .array(
      z.object({
        key: z.string().trim().regex(attributePattern),
        value: contextValueSchema,
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

export type FeatureScope = z.infer<typeof scopeSchema>;
export type FeatureValueInput = z.infer<typeof featureValueSchema>;
export type FeatureDefinitionInput = z.infer<typeof featureDefinitionSchema>;
export type FeatureFlagRecord = z.infer<typeof featureFlagSchema>;
export type FeatureRevisionRecord = z.infer<typeof revisionSchema>;
export type FeatureValidationResult = z.infer<typeof validationResultSchema>;
export type FeatureEvaluationResult = z.infer<typeof evaluationSchema>;
export type FeatureContextInput = z.input<typeof contextSchema>;
export type FeatureValueKind = z.infer<typeof featureValueKindSchema>;

export async function listFlags(
  scope: FeatureScope,
  options: z.input<typeof pageSchema>,
) {
  const parsedScope = scopeSchema.parse(scope);
  const page = pageSchema.parse(options);
  const response = await flagsBuilder(parsedScope).get({ queryParameters: page });
  return flagsPageSchema.parse(requireResponse(response));
}

export async function getFlag(scope: FeatureScope, flagId: string) {
  const parsedScope = scopeSchema.parse(scope);
  const response = await flagsBuilder(parsedScope).byFlagId(idSchema.parse(flagId)).get();
  return featureFlagSchema.parse(requireResponse(response));
}

export async function createFlag(
  csrfToken: string,
  scope: FeatureScope,
  input: {
    definition: FeatureDefinitionInput;
    description: string;
    displayName: string;
    key: string;
    valueKind: FeatureValueKind;
  },
) {
  const parsedScope = scopeSchema.parse(scope);
  const parsed = z
    .object({
      definition: featureDefinitionSchema,
      description: featureDescriptionSchema,
      displayName: featureDisplayNameSchema,
      key: featureKeySchema,
      valueKind: featureValueKindSchema,
    })
    .parse(input);
  assertDefinitionValueKind(parsed.definition, parsed.valueKind);
  const response = await flagsBuilder(parsedScope, csrfToken).post({
    ...parsed,
    definition: parsed.definition as FeatureDefinition,
  });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function updateFlagDraft(
  csrfToken: string,
  flag: FeatureFlagRecord,
  input: {
    definition: FeatureDefinitionInput;
    description: string;
    displayName: string;
  },
) {
  const current = featureFlagSchema.parse(flag);
  const parsed = z
    .object({
      definition: featureDefinitionSchema,
      description: featureDescriptionSchema,
      displayName: featureDisplayNameSchema,
    })
    .parse(input);
  assertDefinitionValueKind(parsed.definition, current.valueKind);
  const response = await flagsBuilder(current, csrfToken)
    .byFlagId(current.id)
    .draft.patch({
      ...parsed,
      definition: parsed.definition as FeatureDefinition,
      expectedVersion: current.version,
    });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function validateFlagDraft(
  csrfToken: string,
  flag: FeatureFlagRecord,
) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagIdValidate(current.id)
    .post({});
  return validationResultSchema.parse(requireResponse(response));
}

export async function publishFlag(csrfToken: string, flag: FeatureFlagRecord) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagIdPublish(current.id)
    .post({ expectedVersion: current.version });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function listFlagRevisions(
  flag: FeatureFlagRecord,
  pageSize = 50,
  pageToken = "",
) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current)
    .byFlagId(current.id)
    .revisions.get({
      queryParameters: {
        pageSize: z.number().int().min(1).max(100).parse(pageSize),
        pageToken: z.string().max(2048).parse(pageToken),
      },
    });
  return revisionsPageSchema.parse(requireResponse(response));
}

export async function rollbackFlag(
  csrfToken: string,
  flag: FeatureFlagRecord,
  revision: number,
) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagIdRollback(current.id)
    .post({
      expectedVersion: current.version,
      revision: positiveVersionSchema.parse(revision),
    });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function archiveFlag(csrfToken: string, flag: FeatureFlagRecord) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current, csrfToken)
    .byFlagId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function restoreFlag(csrfToken: string, flag: FeatureFlagRecord) {
  const current = featureFlagSchema.parse(flag);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return featureFlagSchema.parse(requireResponse(response));
}

export async function simulateFlag(
  csrfToken: string,
  flag: FeatureFlagRecord,
  context: FeatureContextInput,
  useDraft: boolean,
) {
  const current = featureFlagSchema.parse(flag);
  const parsedContext = contextSchema.parse(context);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagIdSimulate(current.id)
    .post({
      context: parsedContext as EvaluationContext,
      useDraft,
    });
  return evaluationSchema.parse(requireResponse(response));
}

export async function evaluateFlag(
  csrfToken: string,
  flag: FeatureFlagRecord,
  context: FeatureContextInput,
) {
  const current = featureFlagSchema.parse(flag);
  const parsedContext = contextSchema.parse(context);
  const response = await flagsBuilder(current, csrfToken)
    .withFlagKeyEvaluate(current.key)
    .post({
      applicationId: current.applicationId,
      context: parsedContext as EvaluationContext,
      environmentId: current.environmentId,
      expectedKind: current.valueKind,
      flagKey: current.key,
      tenantId: current.tenantId,
    });
  return evaluationSchema.parse(requireResponse(response));
}

export function featureErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The feature operation could not be completed.";
}

export function isFeatureVersionConflict(error: unknown): boolean {
  return featureErrorMessage(error).includes("changed since it was loaded");
}

export function valueKindOf(value: FeatureValueInput): FeatureValueKind {
  if ("booleanValue" in value) return FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN;
  if ("stringValue" in value) return FeatureValueKindObject.FEATURE_VALUE_KIND_STRING;
  if ("integerValue" in value) return FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER;
  if ("doubleValue" in value) return FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE;
  return FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT;
}

function assertDefinitionValueKind(
  definition: FeatureDefinitionInput,
  expectedKind: FeatureValueKind,
) {
  const mismatch = definition.variants.find(
    (variant) => valueKindOf(variant.value) !== expectedKind,
  );
  if (mismatch) {
    throw new Error(`Variant '${mismatch.key}' does not match the flag value type.`);
  }
}

function flagsBuilder(scope: FeatureScope, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(scope.tenantId)
    .applications.byApplicationId(scope.applicationId)
    .environments.byEnvironmentId(scope.environmentId).flags;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The Feature API returned an empty response.");
  }
  return response;
}

function isJsonObject(value: string): boolean {
  try {
    const parsed: unknown = JSON.parse(value);
    return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed);
  } catch {
    return false;
  }
}

function isControlCharacter(value: string): boolean {
  const code = value.charCodeAt(0);
  return code <= 31 || code === 127;
}
