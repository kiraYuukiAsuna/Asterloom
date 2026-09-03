"use client";

import { KeyRound, Plus, RefreshCw, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { FeatureValueKindObject } from "@/lib/api/generated/models";
import {
  featureDefinitionSchema,
  type FeatureDefinitionInput,
  type FeatureValueInput,
  type FeatureValueKind,
} from "@/lib/api/feature-management";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";

export type FeatureDefinitionDraft = {
  allocations: Array<{ end: string; start: string; variantKey: string }>;
  bucketingSalt: string;
  defaultVariantKey: string;
  enabled: boolean;
  prerequisites: Array<{ expectedVariantKey: string; flagKey: string }>;
  targetingRules: Array<{ id: string; segmentId: string; variantKey: string }>;
  variants: Array<{ displayName: string; key: string; rawValue: string }>;
};

export type PrerequisiteFlagOption = {
  displayName: string;
  key: string;
  variants: Array<{ displayName: string; key: string }>;
};

export function createFeatureDefinitionDraft(
  valueKind: FeatureValueKind,
  definition?: FeatureDefinitionInput,
): FeatureDefinitionDraft {
  if (definition) {
    return {
      allocations: definition.allocations.map((allocation) => ({
        end: String(allocation.end),
        start: String(allocation.start),
        variantKey: allocation.variantKey,
      })),
      bucketingSalt: definition.bucketingSalt,
      defaultVariantKey: definition.defaultVariantKey,
      enabled: definition.enabled,
      prerequisites: definition.prerequisites.map((item) => ({ ...item })),
      targetingRules: definition.targetingRules.map((item) => ({ ...item })),
      variants: definition.variants.map((variant) => ({
        displayName: variant.displayName,
        key: variant.key,
        rawValue: formatFeatureValue(variant.value),
      })),
    };
  }

  return {
    allocations: [
      { end: "50000", start: "0", variantKey: "off" },
      { end: "100000", start: "50000", variantKey: "on" },
    ],
    bucketingSalt: createSalt(),
    defaultVariantKey: "off",
    enabled: true,
    prerequisites: [],
    targetingRules: [],
    variants: [
      { displayName: "Off", key: "off", rawValue: defaultRawValue(valueKind, false) },
      { displayName: "On", key: "on", rawValue: defaultRawValue(valueKind, true) },
    ],
  };
}

export function toFeatureDefinitionInput(
  draft: FeatureDefinitionDraft,
  valueKind: FeatureValueKind,
): FeatureDefinitionInput {
  return featureDefinitionSchema.parse({
    allocations: draft.allocations.map((allocation) => ({
      end: parseInteger(allocation.end, "Allocation end"),
      start: parseInteger(allocation.start, "Allocation start"),
      variantKey: allocation.variantKey,
    })),
    bucketingSalt: draft.bucketingSalt,
    defaultVariantKey: draft.defaultVariantKey,
    enabled: draft.enabled,
    prerequisites: draft.prerequisites,
    targetingRules: draft.targetingRules,
    variants: draft.variants.map((variant) => ({
      displayName: variant.displayName,
      key: variant.key,
      value: parseFeatureValue(variant.rawValue, valueKind),
    })),
  });
}

export function parseFeatureValue(
  rawValue: string,
  valueKind: FeatureValueKind,
): FeatureValueInput {
  switch (valueKind) {
    case FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN: {
      const normalized = rawValue.trim().toLowerCase();
      if (normalized !== "true" && normalized !== "false") {
        throw new Error("Boolean variant values must be true or false.");
      }
      return { booleanValue: normalized === "true" };
    }
    case FeatureValueKindObject.FEATURE_VALUE_KIND_STRING:
      return { stringValue: rawValue };
    case FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER: {
      const value = Number(rawValue);
      if (!Number.isSafeInteger(value) || rawValue.trim().length === 0) {
        throw new Error("Integer variant values must be safe whole numbers.");
      }
      return { integerValue: value };
    }
    case FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE: {
      const value = Number(rawValue);
      if (!Number.isFinite(value) || rawValue.trim().length === 0) {
        throw new Error("Double variant values must be finite numbers.");
      }
      return { doubleValue: value };
    }
    case FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT:
      return { objectJson: rawValue };
  }
}

export function formatFeatureValue(value: FeatureValueInput): string {
  if ("booleanValue" in value) return String(value.booleanValue);
  if ("stringValue" in value) return value.stringValue;
  if ("integerValue" in value) return String(value.integerValue);
  if ("doubleValue" in value) return String(value.doubleValue);
  return value.objectJson;
}

export function FeatureDefinitionEditor({
  draft,
  idPrefix,
  onChange,
  prerequisiteFlags,
  segments,
  valueKind,
}: {
  draft: FeatureDefinitionDraft;
  idPrefix: string;
  onChange: (draft: FeatureDefinitionDraft) => void;
  prerequisiteFlags: PrerequisiteFlagOption[];
  segments: Array<{ displayName: string; id: string; key: string }>;
  valueKind: FeatureValueKind;
}) {
  const unusedPrerequisiteFlags = prerequisiteFlags.filter(
    (flag) => !draft.prerequisites.some((prerequisite) => prerequisite.flagKey === flag.key),
  );

  function updateVariant(
    index: number,
    update: Partial<FeatureDefinitionDraft["variants"][number]>,
  ) {
    onChange({
      ...draft,
      variants: replaceAt(draft.variants, index, { ...draft.variants[index], ...update }),
    });
  }

  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
        <label>
          <span className={labelClassName}>{translate("Default variant")}</span>
          <select
            aria-label={translate(`${idPrefix} default variant`)}
            className={inputClassName}
            name={`${idPrefix}DefaultVariant`}
            onChange={(event) =>
              onChange({ ...draft, defaultVariantKey: event.target.value })
            }
            value={draft.defaultVariantKey}
          >
            {draft.variants.map((variant, index) => (
              <option key={`${variant.key}-${index}`} value={variant.key}>
                {variant.key || `Variant ${index + 1}`}
              </option>
            ))}
          </select>
        </label>
        <label className="flex h-10 items-center gap-2 text-sm text-slate-300">
          <input
            checked={draft.enabled}
            name={`${idPrefix}Enabled`}
            onChange={(event) => onChange({ ...draft, enabled: event.target.checked })}
            type="checkbox"
          />
          {translate("Flag enabled")}</label>
      </div>

      <EditorSection
        action={
          <Button
            onClick={() =>
              onChange({
                ...draft,
                variants: [
                  ...draft.variants,
                  {
                    displayName: `Variant ${draft.variants.length + 1}`,
                    key: `variant-${draft.variants.length + 1}`,
                    rawValue: defaultRawValue(valueKind, true),
                  },
                ],
              })
            }
            size="sm"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" /> {translate("Add variant")}</Button>
        }
        description={translate(`All values are stored as ${prettyKind(valueKind)}.`)}
        title={translate("Variants")}
      >
        {draft.variants.map((variant, index) => (
          <div
            className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 md:grid-cols-[minmax(0,0.7fr)_minmax(0,0.9fr)_minmax(0,1.3fr)_auto]"
            data-testid={`${idPrefix}-variant-${index}`}
            key={index}
          >
            <TextField
              label={translate("Key")}
              name={`${idPrefix}VariantKey`}
              onChange={(value) => updateVariant(index, { key: value })}
              value={variant.key}
            />
            <TextField
              label={translate("Display name")}
              name={`${idPrefix}VariantDisplayName`}
              onChange={(value) => updateVariant(index, { displayName: value })}
              value={variant.displayName}
            />
            <label>
              <span className={labelClassName}>{translate("Typed value")}</span>
              {valueKind === FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT ? (
                <textarea
                  aria-label={translate(`${idPrefix} variant ${index + 1} value`)}
                  className={`${inputClassName} min-h-20 py-2 font-mono text-xs`}
                  name={`${idPrefix}VariantValue`}
                  onChange={(event) => updateVariant(index, { rawValue: event.target.value })}
                  value={variant.rawValue}
                />
              ) : (
                <input
                  aria-label={translate(`${idPrefix} variant ${index + 1} value`)}
                  className={inputClassName}
                  name={`${idPrefix}VariantValue`}
                  onChange={(event) => updateVariant(index, { rawValue: event.target.value })}
                  value={variant.rawValue}
                />
              )}
            </label>
            <Button
              aria-label={translate(`Remove variant ${index + 1}`)}
              disabled={draft.variants.length === 1}
              onClick={() => {
                const variants = draft.variants.filter((_, candidate) => candidate !== index);
                onChange({
                  ...draft,
                  defaultVariantKey:
                    draft.defaultVariantKey === variant.key
                      ? (variants[0]?.key ?? "")
                      : draft.defaultVariantKey,
                  variants,
                });
              }}
              size="icon"
              type="button"
              variant="ghost"
            >
              <Trash2 className="size-4" />
            </Button>
          </div>
        ))}
      </EditorSection>

      <EditorSection
        action={
          <Button
            disabled={unusedPrerequisiteFlags.length === 0}
            onClick={() => {
              const flag = unusedPrerequisiteFlags[0];
              if (!flag) return;
              onChange({
                ...draft,
                prerequisites: [
                  ...draft.prerequisites,
                  { expectedVariantKey: flag.variants[0]?.key ?? "", flagKey: flag.key },
                ],
              });
            }}
            size="sm"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" /> {translate("Add prerequisite")}</Button>
        }
        description={translate("Published dependencies must resolve to the expected variant.")}
        title={translate("Prerequisites")}
      >
        {draft.prerequisites.length === 0 ? (
          <EmptyRow text={translate("No prerequisites. This flag evaluates independently.")} />
        ) : (
          draft.prerequisites.map((prerequisite, index) => (
            <div
              className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 sm:grid-cols-[1fr_1fr_auto]"
              key={index}
            >
              <SearchableSelect
                ariaLabel={translate(`${idPrefix} prerequisite ${index + 1} flag`)}
                className={inputClassName}
                emptyLabel={translate("Select a published flag")}
                label={translate("Flag key")}
                labelClassName={labelClassName}
                name={`${idPrefix}PrerequisiteFlagKey`}
                onChange={(value) =>
                  onChange({
                    ...draft,
                    prerequisites: replaceAt(draft.prerequisites, index, {
                      expectedVariantKey:
                        prerequisiteFlags.find((flag) => flag.key === value)?.variants[0]?.key ?? "",
                      flagKey: value,
                    }),
                  })
                }
                options={prerequisiteFlags
                  .filter(
                    (flag) =>
                      flag.key === prerequisite.flagKey ||
                      !draft.prerequisites.some(
                        (item, candidate) => candidate !== index && item.flagKey === flag.key,
                      ),
                  )
                  .map((flag) => ({
                    label: `${flag.displayName} (${flag.key})`,
                    value: flag.key,
                  }))}
                required
                value={prerequisite.flagKey}
              />
              <SearchableSelect
                ariaLabel={translate(`${idPrefix} prerequisite ${index + 1} expected variant`)}
                className={inputClassName}
                disabled={!prerequisite.flagKey}
                emptyLabel={translate("Select an expected variant")}
                label={translate("Expected variant")}
                labelClassName={labelClassName}
                name={`${idPrefix}PrerequisiteVariantKey`}
                onChange={(value) =>
                  onChange({
                    ...draft,
                    prerequisites: replaceAt(draft.prerequisites, index, {
                      ...prerequisite,
                      expectedVariantKey: value,
                    }),
                  })
                }
                options={(prerequisiteFlags.find((flag) => flag.key === prerequisite.flagKey)
                  ?.variants ?? []).map((variant) => ({
                  label: `${variant.displayName} (${variant.key})`,
                  value: variant.key,
                }))}
                required
                value={prerequisite.expectedVariantKey}
              />
              <RemoveButton
                label={translate(`Remove prerequisite ${index + 1}`)}
                onClick={() =>
                  onChange({
                    ...draft,
                    prerequisites: draft.prerequisites.filter(
                      (_, candidate) => candidate !== index,
                    ),
                  })
                }
              />
            </div>
          ))
        )}
      </EditorSection>

      <EditorSection
        action={
          <Button
            disabled={segments.length === 0}
            onClick={() =>
              onChange({
                ...draft,
                targetingRules: [
                  ...draft.targetingRules,
                  {
                    id: `rule-${draft.targetingRules.length + 1}`,
                    segmentId: segments[0]?.id ?? "",
                    variantKey: draft.variants[0]?.key ?? "",
                  },
                ],
              })
            }
            size="sm"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" /> {translate("Add segment rule")}</Button>
        }
        description={translate("Rules run top-to-bottom and stop at the first matching active segment.")}
        title={translate("Targeting rules")}
      >
        {draft.targetingRules.length === 0 ? (
          <EmptyRow
            text={
              translate(segments.length === 0
                ? "Create an active Targeting segment before adding a rule."
                : "No segment overrides. Evaluation proceeds to allocations.")
            }
          />
        ) : (
          draft.targetingRules.map((rule, index) => (
            <div
              className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 md:grid-cols-[0.8fr_1.4fr_1fr_auto]"
              key={index}
            >
              <TextField
                label={translate("Rule ID")}
                name={`${idPrefix}TargetingRuleId`}
                onChange={(value) =>
                  onChange({
                    ...draft,
                    targetingRules: replaceAt(draft.targetingRules, index, {
                      ...rule,
                      id: value,
                    }),
                  })
                }
                value={rule.id}
              />
              <label>
                <span className={labelClassName}>{translate("Active segment")}</span>
                <select
                  className={inputClassName}
                  name={`${idPrefix}TargetingSegment`}
                  onChange={(event) =>
                    onChange({
                      ...draft,
                      targetingRules: replaceAt(draft.targetingRules, index, {
                        ...rule,
                        segmentId: event.target.value,
                      }),
                    })
                  }
                  value={rule.segmentId}
                >
                  {segments.map((segment) => (
                    <option key={segment.id} value={segment.id}>
                      {segment.displayName} ({segment.key})
                    </option>
                  ))}
                </select>
              </label>
              <VariantSelect
                idPrefix={`${idPrefix}Targeting`}
                label={translate("Serve variant")}
                onChange={(variantKey) =>
                  onChange({
                    ...draft,
                    targetingRules: replaceAt(draft.targetingRules, index, {
                      ...rule,
                      variantKey,
                    }),
                  })
                }
                value={rule.variantKey}
                variants={draft.variants}
              />
              <RemoveButton
                label={translate(`Remove targeting rule ${index + 1}`)}
                onClick={() =>
                  onChange({
                    ...draft,
                    targetingRules: draft.targetingRules.filter(
                      (_, candidate) => candidate !== index,
                    ),
                  })
                }
              />
            </div>
          ))
        )}
      </EditorSection>

      <EditorSection
        action={
          <Button
            onClick={() =>
              onChange({
                ...draft,
                allocations: [
                  ...draft.allocations,
                  {
                    end: "100000",
                    start: draft.allocations.at(-1)?.end ?? "0",
                    variantKey: draft.variants[0]?.key ?? "",
                  },
                ],
              })
            }
            size="sm"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" /> {translate("Add allocation")}</Button>
        }
        description={translate("Deterministic v1 buckets use the half-open range [start, end) out of 100,000.")}
        title={translate("Rollout allocation")}
      >
        {draft.allocations.length === 0 ? (
          <EmptyRow text={translate("No rollout allocation. Unmatched contexts receive the default variant.")} />
        ) : (
          draft.allocations.map((allocation, index) => (
            <div
              className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 sm:grid-cols-[1fr_8rem_8rem_auto]"
              key={index}
            >
              <VariantSelect
                idPrefix={`${idPrefix}Allocation`}
                label={translate("Variant")}
                onChange={(variantKey) =>
                  onChange({
                    ...draft,
                    allocations: replaceAt(draft.allocations, index, {
                      ...allocation,
                      variantKey,
                    }),
                  })
                }
                value={allocation.variantKey}
                variants={draft.variants}
              />
              <TextField
                label={translate("Start")}
                name={`${idPrefix}AllocationStart`}
                onChange={(start) =>
                  onChange({
                    ...draft,
                    allocations: replaceAt(draft.allocations, index, {
                      ...allocation,
                      start,
                    }),
                  })
                }
                type="number"
                value={allocation.start}
              />
              <TextField
                label={translate("End")}
                name={`${idPrefix}AllocationEnd`}
                onChange={(end) =>
                  onChange({
                    ...draft,
                    allocations: replaceAt(draft.allocations, index, {
                      ...allocation,
                      end,
                    }),
                  })
                }
                type="number"
                value={allocation.end}
              />
              <RemoveButton
                label={translate(`Remove allocation ${index + 1}`)}
                onClick={() =>
                  onChange({
                    ...draft,
                    allocations: draft.allocations.filter(
                      (_, candidate) => candidate !== index,
                    ),
                  })
                }
              />
            </div>
          ))
        )}
      </EditorSection>

      <EditorSection
        action={
          <Button
            onClick={() => {
              if (
                window.confirm(
                  translate("Changing the salt reshuffles rollout buckets. Continue with a new salt?"),
                )
              ) {
                onChange({ ...draft, bucketingSalt: createSalt() });
              }
            }}
            size="sm"
            type="button"
            variant="outline"
          >
            <RefreshCw className="size-3.5" /> {translate("Generate new salt")}</Button>
        }
        description={translate("Keep this stable to preserve each targeting key's bucket assignment.")}
        title={translate("Bucketing identity")}
      >
        <label>
          <span className={labelClassName}>{translate("Stable salt")}</span>
          <div className="relative">
            <KeyRound className="absolute left-3 top-3 size-4 text-slate-600" />
            <input
              className={`${inputClassName} pl-10 font-mono text-xs`}
              name={`${idPrefix}BucketingSalt`}
              onChange={(event) =>
                onChange({ ...draft, bucketingSalt: event.target.value })
              }
              value={draft.bucketingSalt}
            />
          </div>
        </label>
      </EditorSection>
    </div>
  );
}

function EditorSection({
  action,
  children,
  description,
  title,
}: {
  action: React.ReactNode;
  children: React.ReactNode;
  description: string;
  title: string;
}) {
  return (
    <section className="rounded-2xl border border-white/8 bg-black/10 p-4 sm:p-5">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-slate-200">{title}</h3>
          <p className="mt-1 text-xs leading-5 text-slate-500">{description}</p>
        </div>
        {action}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

function TextField({
  label,
  name,
  onChange,
  type = "text",
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  type?: "number" | "text";
  value: string;
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <input
        className={inputClassName}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        type={type}
        value={value}
      />
    </label>
  );
}

function VariantSelect({
  idPrefix,
  label,
  onChange,
  value,
  variants,
}: {
  idPrefix: string;
  label: string;
  onChange: (value: string) => void;
  value: string;
  variants: FeatureDefinitionDraft["variants"];
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <select
        className={inputClassName}
        name={`${idPrefix}VariantKey`}
        onChange={(event) => onChange(event.target.value)}
        value={value}
      >
        {variants.map((variant, index) => (
          <option key={`${variant.key}-${index}`} value={variant.key}>
            {variant.key || `Variant ${index + 1}`}
          </option>
        ))}
      </select>
    </label>
  );
}

function RemoveButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <Button
      aria-label={label}
      className="self-end"
      onClick={onClick}
      size="icon"
      type="button"
      variant="ghost"
    >
      <Trash2 className="size-4" />
    </Button>
  );
}

function EmptyRow({ text }: { text: string }) {
  return (
    <p className="rounded-xl border border-dashed border-white/10 px-4 py-5 text-center text-xs text-slate-600">
      {text}
    </p>
  );
}

function replaceAt<T>(values: T[], index: number, value: T): T[] {
  return values.map((candidate, candidateIndex) =>
    candidateIndex === index ? value : candidate,
  );
}

function parseInteger(value: string, label: string): number {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || value.trim().length === 0) {
    throw new Error(`${label} must be an integer.`);
  }
  return parsed;
}

function defaultRawValue(valueKind: FeatureValueKind, positive: boolean): string {
  switch (valueKind) {
    case FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN:
      return String(positive);
    case FeatureValueKindObject.FEATURE_VALUE_KIND_STRING:
      return positive ? "enabled" : "disabled";
    case FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER:
      return positive ? "1" : "0";
    case FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE:
      return positive ? "1.0" : "0.0";
    case FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT:
      return positive ? '{"enabled":true}' : '{"enabled":false}';
  }
}

function prettyKind(valueKind: FeatureValueKind): string {
  return translate(valueKind.replace("FEATURE_VALUE_KIND_", "").toLowerCase());
}

function createSalt(): string {
  return globalThis.crypto?.randomUUID?.().replaceAll("-", "") ?? "stable-salt-v1";
}
