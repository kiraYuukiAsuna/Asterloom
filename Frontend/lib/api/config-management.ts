import { z } from "zod";

import {
  ConfigEvaluationReasonObject,
  ConfigResourceStatusObject,
  ConfigValidationSeverityObject,
  ConfigValueKindObject,
  ConfigVisibilityObject,
  type ConfigDefinition,
  type EvaluationContext,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const positiveVersionSchema = z.number().int().positive();
const stableKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const ruleIdPattern = /^[a-zA-Z0-9](?:[a-zA-Z0-9._-]{0,98}[a-zA-Z0-9])?$/;
const attributePattern = /^[a-z][a-zA-Z0-9_.-]{0,63}$/;

export const configValueKindSchema = z.enum([
  ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN,
  ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER,
  ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE,
  ConfigValueKindObject.CONFIG_VALUE_KIND_STRING,
  ConfigValueKindObject.CONFIG_VALUE_KIND_JSON,
]);
export const configVisibilitySchema = z.enum([
  ConfigVisibilityObject.CONFIG_VISIBILITY_CLIENT,
  ConfigVisibilityObject.CONFIG_VISIBILITY_SERVER,
]);
const resourceStatusSchema = z.enum([
  ConfigResourceStatusObject.CONFIG_RESOURCE_STATUS_ACTIVE,
  ConfigResourceStatusObject.CONFIG_RESOURCE_STATUS_ARCHIVED,
]);
const validationSeveritySchema = z.enum([
  ConfigValidationSeverityObject.CONFIG_VALIDATION_SEVERITY_ERROR,
  ConfigValidationSeverityObject.CONFIG_VALIDATION_SEVERITY_WARNING,
]);
const evaluationReasonSchema = z.enum([
  ConfigEvaluationReasonObject.CONFIG_EVALUATION_REASON_TARGETING_MATCH,
  ConfigEvaluationReasonObject.CONFIG_EVALUATION_REASON_DEFAULT,
]);

export const configKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(stableKeyPattern, "Use 1–100 lowercase letters, numbers, periods, underscores, or hyphens.");
export const configDisplayNameSchema = z.string().trim().min(1).max(200);
export const configDescriptionSchema = z.string().trim().max(1000);

export const configValueSchema = z.union([
  z.object({ booleanValue: z.boolean() }),
  z.object({ integerValue: z.number().int().safe() }),
  z.object({ doubleValue: z.number().finite() }),
  z.object({ stringValue: z.string().max(100_000) }),
  z.object({
    jsonValue: z.string().max(250_000).refine(isJsonObject, "Enter a JSON object."),
  }),
]);

const targetingRuleSchema = z.object({
  id: z.string().trim().regex(ruleIdPattern, "Enter a stable rule ID."),
  segmentId: idSchema,
  value: configValueSchema,
});

export const configDefinitionSchema = z
  .object({
    defaultValue: configValueSchema,
    schemaJson: z
      .string()
      .trim()
      .max(100_000)
      .refine((value) => value.length === 0 || isJsonObject(value), "Enter a JSON Schema object."),
    targetingRules: z.array(targetingRuleSchema).max(50),
  })
  .superRefine((definition, context) => {
    const kind = valueKindOf(definition.defaultValue);
    const ids = new Set<string>();
    definition.targetingRules.forEach((rule, index) => {
      if (ids.has(rule.id)) {
        context.addIssue({
          code: "custom",
          message: `Rule ID '${rule.id}' is duplicated.`,
          path: ["targetingRules", index, "id"],
        });
      }
      ids.add(rule.id);
      if (valueKindOf(rule.value) !== kind) {
        context.addIssue({
          code: "custom",
          message: "Targeted value must use the same type as the default value.",
          path: ["targetingRules", index, "value"],
        });
      }
    });
  });

const entrySchema = z.object({
  applicationId: idSchema,
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: z.string(),
  displayName: configDisplayNameSchema,
  draftDefinition: configDefinitionSchema,
  draftRevision: positiveVersionSchema,
  environmentId: idSchema,
  id: idSchema,
  key: configKeySchema,
  publishedAt: timestampSchema.nullish(),
  publishedDefinition: configDefinitionSchema.nullish(),
  publishedRevision: z.number().int().nonnegative(),
  publishedSnapshotVersion: z.number().int().nonnegative(),
  status: resourceStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  valueKind: configValueKindSchema,
  version: positiveVersionSchema,
  visibility: configVisibilitySchema,
});
const entriesPageSchema = z.object({
  entries: z.array(entrySchema),
  nextPageToken: z.string().nullish(),
});
const revisionSchema = z.object({
  definition: configDefinitionSchema,
  entryId: idSchema,
  id: idSchema,
  publishedAt: timestampSchema,
  revision: positiveVersionSchema,
  snapshotVersion: positiveVersionSchema,
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
const diffSchema = z.object({
  changed: z.boolean(),
  changedPaths: z.array(z.string()),
  draftJson: z.string(),
  publishedJson: z.string(),
});
const effectiveValueSchema = z.object({
  entryId: idSchema,
  key: configKeySchema,
  reason: evaluationReasonSchema,
  revision: positiveVersionSchema,
  targetingRuleId: z.string().nullish(),
  value: configValueSchema,
  valueKind: configValueKindSchema,
});
const snapshotMetadataSchema = z.object({
  applicationId: idSchema,
  createdAt: timestampSchema,
  entryCount: z.number().int().nonnegative(),
  environmentId: idSchema,
  id: idSchema,
  tenantId: idSchema,
  version: positiveVersionSchema,
});
const snapshotsPageSchema = z.object({
  nextPageToken: z.string().nullish(),
  snapshots: z.array(snapshotMetadataSchema),
});
const snapshotSchema = z.object({
  etag: z.string().min(1),
  generatedAt: timestampSchema,
  notModified: z.boolean(),
  snapshotVersion: z.number().int().nonnegative(),
  values: z.array(effectiveValueSchema),
});
const updateStatusSchema = z.object({
  changed: z.boolean(),
  checkedAt: timestampSchema,
  currentSnapshotVersion: z.number().int().nonnegative(),
  etag: z.string().min(1),
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
    .array(z.object({ key: z.string().trim().regex(attributePattern), value: contextValueSchema }))
    .max(64)
    .default([]),
  clientVersion: z.string().trim().max(100).default(""),
  language: z.string().trim().max(100).default(""),
  platform: z.string().trim().max(100).default(""),
  region: z.string().trim().max(100).default(""),
  targetingKey: z.string().trim().min(1).max(512),
  userId: z.string().trim().max(200).default(""),
});

export type ConfigScope = z.infer<typeof scopeSchema>;
export type ConfigValueInput = z.infer<typeof configValueSchema>;
export type ConfigDefinitionInput = z.infer<typeof configDefinitionSchema>;
export type ConfigEntryRecord = z.infer<typeof entrySchema>;
export type ConfigRevisionRecord = z.infer<typeof revisionSchema>;
export type ConfigValidationResult = z.infer<typeof validationResultSchema>;
export type ConfigDiffResult = z.infer<typeof diffSchema>;
export type ConfigEffectiveValueRecord = z.infer<typeof effectiveValueSchema>;
export type ConfigSnapshotRecord = z.infer<typeof snapshotSchema>;
export type ConfigContextInput = z.input<typeof contextSchema>;
export type ConfigValueKind = z.infer<typeof configValueKindSchema>;
export type ConfigVisibility = z.infer<typeof configVisibilitySchema>;

export async function listConfigEntries(scope: ConfigScope, options: z.input<typeof pageSchema>) {
  const parsedScope = scopeSchema.parse(scope);
  const response = await entriesBuilder(parsedScope).get({
    queryParameters: pageSchema.parse(options),
  });
  return entriesPageSchema.parse(requireResponse(response));
}

export async function getConfigEntry(scope: ConfigScope, entryId: string) {
  const response = await entriesBuilder(scopeSchema.parse(scope))
    .byEntryId(idSchema.parse(entryId))
    .get();
  return entrySchema.parse(requireResponse(response));
}

export async function createConfigEntry(
  csrfToken: string,
  scope: ConfigScope,
  input: {
    definition: ConfigDefinitionInput;
    description: string;
    displayName: string;
    key: string;
    valueKind: ConfigValueKind;
    visibility: ConfigVisibility;
  },
) {
  const parsedScope = scopeSchema.parse(scope);
  const parsed = z
    .object({
      definition: configDefinitionSchema,
      description: configDescriptionSchema,
      displayName: configDisplayNameSchema,
      key: configKeySchema,
      valueKind: configValueKindSchema,
      visibility: configVisibilitySchema,
    })
    .parse(input);
  assertDefinitionValueKind(parsed.definition, parsed.valueKind);
  const response = await entriesBuilder(parsedScope, csrfToken).post({
    ...parsed,
    definition: parsed.definition as ConfigDefinition,
  });
  return entrySchema.parse(requireResponse(response));
}

export async function updateConfigDraft(
  csrfToken: string,
  entry: ConfigEntryRecord,
  input: {
    definition: ConfigDefinitionInput;
    description: string;
    displayName: string;
    visibility: ConfigVisibility;
  },
) {
  const current = entrySchema.parse(entry);
  const parsed = z
    .object({
      definition: configDefinitionSchema,
      description: configDescriptionSchema,
      displayName: configDisplayNameSchema,
      visibility: configVisibilitySchema,
    })
    .parse(input);
  assertDefinitionValueKind(parsed.definition, current.valueKind);
  const response = await entriesBuilder(current, csrfToken)
    .byEntryId(current.id)
    .draft.patch({
      ...parsed,
      definition: parsed.definition as ConfigDefinition,
      expectedVersion: current.version,
    });
  return entrySchema.parse(requireResponse(response));
}

export async function validateConfigDraft(csrfToken: string, entry: ConfigEntryRecord) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current, csrfToken)
    .withEntryIdValidate(current.id)
    .post({});
  return validationResultSchema.parse(requireResponse(response));
}

export async function diffConfigDraft(entry: ConfigEntryRecord) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current).byEntryId(current.id).diff.get();
  return diffSchema.parse(requireResponse(response));
}

export async function publishConfigEntry(csrfToken: string, entry: ConfigEntryRecord) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current, csrfToken)
    .withEntryIdPublish(current.id)
    .post({ expectedVersion: current.version });
  return entrySchema.parse(requireResponse(response));
}

export async function listConfigRevisions(
  entry: ConfigEntryRecord,
  pageSize = 50,
  pageToken = "",
) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current)
    .byEntryId(current.id)
    .revisions.get({
      queryParameters: {
        pageSize: z.number().int().min(1).max(100).parse(pageSize),
        pageToken: z.string().max(2048).parse(pageToken),
      },
    });
  return revisionsPageSchema.parse(requireResponse(response));
}

export async function rollbackConfigEntry(
  csrfToken: string,
  entry: ConfigEntryRecord,
  revision: number,
) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current, csrfToken)
    .withEntryIdRollback(current.id)
    .post({ expectedVersion: current.version, revision: positiveVersionSchema.parse(revision) });
  return entrySchema.parse(requireResponse(response));
}

export async function archiveConfigEntry(csrfToken: string, entry: ConfigEntryRecord) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current, csrfToken)
    .byEntryId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return entrySchema.parse(requireResponse(response));
}

export async function restoreConfigEntry(csrfToken: string, entry: ConfigEntryRecord) {
  const current = entrySchema.parse(entry);
  const response = await entriesBuilder(current, csrfToken)
    .withEntryIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return entrySchema.parse(requireResponse(response));
}

export async function previewConfigValue(
  csrfToken: string,
  entry: ConfigEntryRecord,
  context: ConfigContextInput,
  useDraft: boolean,
) {
  const current = entrySchema.parse(entry);
  const parsedContext = contextSchema.parse(context);
  const response = await entriesBuilder(current, csrfToken)
    .withEntryIdPreview(current.id)
    .post({ context: parsedContext as EvaluationContext, useDraft });
  return effectiveValueSchema.parse(requireResponse(response));
}

export async function listConfigSnapshots(
  scope: ConfigScope,
  pageSize = 50,
  pageToken = "",
) {
  const parsed = scopeSchema.parse(scope);
  const response = await configBuilder(parsed).snapshots.get({
    queryParameters: {
      pageSize: z.number().int().min(1).max(100).parse(pageSize),
      pageToken: z.string().max(2048).parse(pageToken),
    },
  });
  return snapshotsPageSchema.parse(requireResponse(response));
}

export async function getConfigSnapshot(
  csrfToken: string,
  scope: ConfigScope,
  context: ConfigContextInput,
  ifNoneMatch = "",
  includeServerValues = false,
) {
  const parsed = scopeSchema.parse(scope);
  const parsedContext = contextSchema.parse(context);
  const environment = environmentBuilder(parsed, csrfToken);
  const request = {
    applicationId: parsed.applicationId,
    context: parsedContext as EvaluationContext,
    environmentId: parsed.environmentId,
    ifNoneMatch: z.string().max(512).parse(ifNoneMatch),
    tenantId: parsed.tenantId,
  };
  const response = includeServerValues
    ? await environment.configServerSnapshot.post(request)
    : await environment.configSnapshot.post(request);
  return snapshotSchema.parse(requireResponse(response));
}

export async function checkConfigUpdates(
  csrfToken: string,
  scope: ConfigScope,
  context: ConfigContextInput,
  knownSnapshotVersion: number,
) {
  const parsed = scopeSchema.parse(scope);
  const parsedContext = contextSchema.parse(context);
  const response = await environmentBuilder(parsed, csrfToken).configCheckUpdates.post({
    applicationId: parsed.applicationId,
    context: parsedContext as EvaluationContext,
    environmentId: parsed.environmentId,
    knownSnapshotVersion: z.number().int().nonnegative().parse(knownSnapshotVersion),
    tenantId: parsed.tenantId,
  });
  return updateStatusSchema.parse(requireResponse(response));
}

export function configErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The configuration operation could not be completed.";
}

export function isConfigVersionConflict(error: unknown): boolean {
  return configErrorMessage(error).includes("changed since it was loaded");
}

export function valueKindOf(value: ConfigValueInput): ConfigValueKind {
  if ("booleanValue" in value) return ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN;
  if ("integerValue" in value) return ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER;
  if ("doubleValue" in value) return ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE;
  if ("stringValue" in value) return ConfigValueKindObject.CONFIG_VALUE_KIND_STRING;
  return ConfigValueKindObject.CONFIG_VALUE_KIND_JSON;
}

function assertDefinitionValueKind(
  definition: ConfigDefinitionInput,
  expectedKind: ConfigValueKind,
) {
  if (valueKindOf(definition.defaultValue) !== expectedKind) {
    throw new Error("The default value does not match the configuration value type.");
  }
  if (definition.targetingRules.some((rule) => valueKindOf(rule.value) !== expectedKind)) {
    throw new Error("A targeted value does not match the configuration value type.");
  }
}

function environmentBuilder(scope: ConfigScope, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(scope.tenantId)
    .applications.byApplicationId(scope.applicationId)
    .environments.byEnvironmentId(scope.environmentId);
}

function configBuilder(scope: ConfigScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).config;
}

function entriesBuilder(scope: ConfigScope, csrfToken?: string) {
  return configBuilder(scope, csrfToken).entries;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Config API returned an empty response.");
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
