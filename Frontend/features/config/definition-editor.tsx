"use client";

import { Plus, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { ConfigValueKindObject } from "@/lib/api/generated/models";
import {
  configDefinitionSchema,
  type ConfigDefinitionInput,
  type ConfigValueInput,
  type ConfigValueKind,
} from "@/lib/api/config-management";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";

export type ConfigDefinitionDraft = {
  defaultRawValue: string;
  schemaJson: string;
  targetingRules: Array<{ id: string; rawValue: string; segmentId: string }>;
};

export function createConfigDefinitionDraft(
  valueKind: ConfigValueKind,
  definition?: ConfigDefinitionInput,
): ConfigDefinitionDraft {
  if (definition) {
    return {
      defaultRawValue: formatConfigValue(definition.defaultValue),
      schemaJson: definition.schemaJson,
      targetingRules: definition.targetingRules.map((rule) => ({
        id: rule.id,
        rawValue: formatConfigValue(rule.value),
        segmentId: rule.segmentId,
      })),
    };
  }
  return {
    defaultRawValue: defaultRawValue(valueKind),
    schemaJson: defaultSchema(valueKind),
    targetingRules: [],
  };
}

export function toConfigDefinitionInput(
  draft: ConfigDefinitionDraft,
  valueKind: ConfigValueKind,
): ConfigDefinitionInput {
  return configDefinitionSchema.parse({
    defaultValue: parseConfigValue(draft.defaultRawValue, valueKind),
    schemaJson: draft.schemaJson,
    targetingRules: draft.targetingRules.map((rule) => ({
      id: rule.id,
      segmentId: rule.segmentId,
      value: parseConfigValue(rule.rawValue, valueKind),
    })),
  });
}

export function parseConfigValue(
  rawValue: string,
  valueKind: ConfigValueKind,
): ConfigValueInput {
  switch (valueKind) {
    case ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN: {
      const normalized = rawValue.trim().toLowerCase();
      if (normalized !== "true" && normalized !== "false") {
        throw new Error("Boolean configuration values must be true or false.");
      }
      return { booleanValue: normalized === "true" };
    }
    case ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER: {
      const value = Number(rawValue);
      if (!Number.isSafeInteger(value) || rawValue.trim().length === 0) {
        throw new Error("Integer configuration values must be safe whole numbers.");
      }
      return { integerValue: value };
    }
    case ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE: {
      const value = Number(rawValue);
      if (!Number.isFinite(value) || rawValue.trim().length === 0) {
        throw new Error("Double configuration values must be finite numbers.");
      }
      return { doubleValue: value };
    }
    case ConfigValueKindObject.CONFIG_VALUE_KIND_STRING:
      return { stringValue: rawValue };
    case ConfigValueKindObject.CONFIG_VALUE_KIND_JSON:
      return { jsonValue: rawValue };
  }
}

export function formatConfigValue(value: ConfigValueInput): string {
  if ("booleanValue" in value) return String(value.booleanValue);
  if ("integerValue" in value) return String(value.integerValue);
  if ("doubleValue" in value) return String(value.doubleValue);
  if ("stringValue" in value) return value.stringValue;
  return value.jsonValue;
}

export function ConfigDefinitionEditor({
  draft,
  idPrefix,
  onChange,
  segments,
  valueKind,
}: {
  draft: ConfigDefinitionDraft;
  idPrefix: string;
  onChange: (draft: ConfigDefinitionDraft) => void;
  segments: Array<{ displayName: string; id: string; key: string }>;
  valueKind: ConfigValueKind;
}) {
  return (
    <div className="space-y-5">
      <div className="grid gap-4 lg:grid-cols-2">
        <ValueField
          label={translate("Default value")}
          name={`${idPrefix}DefaultValue`}
          onChange={(defaultRawValue) => onChange({ ...draft, defaultRawValue })}
          value={draft.defaultRawValue}
          valueKind={valueKind}
        />
        <div className="rounded-xl border border-white/8 bg-white/[0.025] p-4 text-xs text-slate-400">
          <p className="font-semibold text-slate-200">{translate("Typed as")}{" "}{prettyKind(valueKind)}</p>
          <p className="mt-1 leading-5">
            {translate("Every default and targeted value must keep this type. Published snapshots are immutable.")}</p>
        </div>
      </div>

      <label>
        <span className={labelClassName}>{translate("JSON Schema (supported subset)")}</span>
        <textarea
          aria-label={translate(`${idPrefix} JSON Schema`)}
          className={`${inputClassName} min-h-28 py-2 font-mono text-xs`}
          name={`${idPrefix}SchemaJson`}
          onChange={(event) => onChange({ ...draft, schemaJson: event.target.value })}
          spellCheck={false}
          value={draft.schemaJson}
        />
      </label>

      <section className="space-y-3">
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="text-sm font-semibold text-slate-200">{translate("Targeting overrides")}</p>
            <p className="mt-1 text-xs text-slate-500">
              {translate("First matching segment wins; the segment rule is captured at publish time.")}</p>
          </div>
          <Button
            disabled={segments.length === 0 || draft.targetingRules.length >= 50}
            onClick={() =>
              onChange({
                ...draft,
                targetingRules: [
                  ...draft.targetingRules,
                  {
                    id: `rule-${draft.targetingRules.length + 1}`,
                    rawValue: defaultRawValue(valueKind),
                    segmentId: segments[0]?.id ?? "",
                  },
                ],
              })
            }
            size="sm"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" /> {translate("Add segment override")}</Button>
        </div>
        {draft.targetingRules.length === 0 ? (
          <div className="rounded-xl border border-dashed border-white/10 px-4 py-5 text-center text-xs text-slate-500">
            {translate("No override. Every context receives the default value.")}</div>
        ) : (
          draft.targetingRules.map((rule, index) => (
            <div
              className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 lg:grid-cols-[minmax(0,.65fr)_minmax(0,1fr)_minmax(0,1fr)_auto]"
              data-testid={`${idPrefix}-targeting-rule-${index}`}
              key={index}
            >
              <TextField
                label={translate("Rule ID")}
                name={`${idPrefix}TargetingRuleId`}
                onChange={(id) => updateRule(draft, index, { id }, onChange)}
                value={rule.id}
              />
              <label>
                <span className={labelClassName}>{translate("Segment")}</span>
                <select
                  aria-label={translate(`${idPrefix} targeting segment ${index + 1}`)}
                  className={inputClassName}
                  name={`${idPrefix}TargetingSegment`}
                  onChange={(event) =>
                    updateRule(draft, index, { segmentId: event.target.value }, onChange)
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
              <ValueField
                label={translate("Override value")}
                name={`${idPrefix}TargetingValue`}
                onChange={(rawValue) => updateRule(draft, index, { rawValue }, onChange)}
                value={rule.rawValue}
                valueKind={valueKind}
              />
              <Button
                aria-label={translate(`Remove targeting override ${index + 1}`)}
                onClick={() =>
                  onChange({
                    ...draft,
                    targetingRules: draft.targetingRules.filter(
                      (_, candidate) => candidate !== index,
                    ),
                  })
                }
                size="icon"
                type="button"
                variant="ghost"
              >
                <Trash2 className="size-4" />
              </Button>
            </div>
          ))
        )}
      </section>
    </div>
  );
}

function ValueField({
  label,
  name,
  onChange,
  value,
  valueKind,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  value: string;
  valueKind: ConfigValueKind;
}) {
  const multiline = valueKind === ConfigValueKindObject.CONFIG_VALUE_KIND_JSON;
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      {multiline ? (
        <textarea
          aria-label={label}
          className={`${inputClassName} min-h-20 py-2 font-mono text-xs`}
          name={name}
          onChange={(event) => onChange(event.target.value)}
          spellCheck={false}
          value={value}
        />
      ) : (
        <input
          aria-label={label}
          className={inputClassName}
          name={name}
          onChange={(event) => onChange(event.target.value)}
          value={value}
        />
      )}
    </label>
  );
}

function TextField({
  label,
  name,
  onChange,
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  value: string;
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <input
        aria-label={label}
        className={inputClassName}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        value={value}
      />
    </label>
  );
}

function updateRule(
  draft: ConfigDefinitionDraft,
  index: number,
  update: Partial<ConfigDefinitionDraft["targetingRules"][number]>,
  onChange: (draft: ConfigDefinitionDraft) => void,
) {
  onChange({
    ...draft,
    targetingRules: draft.targetingRules.map((rule, candidate) =>
      candidate === index ? { ...rule, ...update } : rule,
    ),
  });
}

function defaultRawValue(valueKind: ConfigValueKind): string {
  switch (valueKind) {
    case ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN:
      return "false";
    case ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER:
      return "0";
    case ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE:
      return "0.0";
    case ConfigValueKindObject.CONFIG_VALUE_KIND_STRING:
      return "";
    case ConfigValueKindObject.CONFIG_VALUE_KIND_JSON:
      return "{}";
  }
}

function defaultSchema(valueKind: ConfigValueKind): string {
  const type = {
    [ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN]: "boolean",
    [ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER]: "integer",
    [ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE]: "number",
    [ConfigValueKindObject.CONFIG_VALUE_KIND_STRING]: "string",
    [ConfigValueKindObject.CONFIG_VALUE_KIND_JSON]: "object",
  }[valueKind];
  return JSON.stringify({ type }, null, 2);
}

function prettyKind(valueKind: ConfigValueKind): string {
  return translate(valueKind.replace("CONFIG_VALUE_KIND_", "").toLowerCase());
}
