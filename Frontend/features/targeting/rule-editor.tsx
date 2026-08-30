"use client";

import { Plus, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import {
  TargetingMatchModeObject,
  TargetingOperatorObject,
  TargetingValueKindObject,
} from "@/lib/api/generated/models";
import type {
  TargetingCatalog,
  TargetingConditionInput,
  TargetingRuleInput,
  TargetingSegmentRecord,
  TargetingValueInput,
} from "@/lib/api/targeting-management";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";

const existsOperators = new Set<string>([
  TargetingOperatorObject.TARGETING_OPERATOR_EXISTS,
  TargetingOperatorObject.TARGETING_OPERATOR_NOT_EXISTS,
]);
const multipleValueOperators = new Set<string>([
  TargetingOperatorObject.TARGETING_OPERATOR_ONE_OF,
  TargetingOperatorObject.TARGETING_OPERATOR_NOT_ONE_OF,
]);

type ValueKind = TargetingConditionInput["valueKind"];
type Operator = TargetingConditionInput["operator"];

export type TargetingConditionDraft = {
  attribute: string;
  caseSensitive: boolean;
  id: string;
  operator: Operator;
  rawValues: string;
  valueKind: ValueKind;
};

export type TargetingRuleDraft = {
  conditions: TargetingConditionDraft[];
  matchMode: TargetingRuleInput["matchMode"];
};

export function createRuleDraft(
  rule?: TargetingSegmentRecord["rule"],
): TargetingRuleDraft {
  if (!rule) {
    return {
      conditions: [createConditionDraft(1)],
      matchMode: TargetingMatchModeObject.TARGETING_MATCH_MODE_ALL,
    };
  }

  return {
    conditions: rule.conditions.map((condition) => ({
      attribute: condition.attribute,
      caseSensitive: condition.caseSensitive,
      id: condition.id,
      operator: condition.operator,
      rawValues: condition.values.map(formatValue).join(", "),
      valueKind: condition.valueKind,
    })),
    matchMode: rule.matchMode,
  };
}

export function toRuleInput(draft: TargetingRuleDraft): TargetingRuleInput {
  return {
    conditions: draft.conditions.map((condition) => ({
      attribute: condition.attribute,
      caseSensitive: condition.caseSensitive,
      id: condition.id,
      operator: condition.operator,
      valueKind: condition.valueKind,
      values: parseValues(condition),
    })),
    matchMode: draft.matchMode,
  };
}

export function RuleEditor({
  catalog,
  draft,
  idPrefix,
  onChange,
}: {
  catalog?: TargetingCatalog;
  draft: TargetingRuleDraft;
  idPrefix: string;
  onChange: (draft: TargetingRuleDraft) => void;
}) {
  function updateCondition(
    index: number,
    update: Partial<TargetingConditionDraft>,
  ) {
    onChange({
      ...draft,
      conditions: draft.conditions.map((condition, candidateIndex) =>
        candidateIndex === index ? { ...condition, ...update } : condition,
      ),
    });
  }

  function changeValueKind(index: number, valueKind: ValueKind) {
    const supportedOperators = catalog?.operators.filter((metadata) =>
      metadata.supportedValueKinds.includes(valueKind),
    );
    const current = draft.conditions[index];
    const operator =
      supportedOperators?.some((metadata) => metadata.operator === current.operator)
        ? current.operator
        : (supportedOperators?.[0]?.operator ??
          TargetingOperatorObject.TARGETING_OPERATOR_EQUALS);
    updateCondition(index, { operator, rawValues: "", valueKind });
  }

  function removeCondition(index: number) {
    if (draft.conditions.length === 1) {
      return;
    }
    onChange({
      ...draft,
      conditions: draft.conditions.filter((_, candidateIndex) => candidateIndex !== index),
    });
  }

  return (
    <div className="space-y-4">
      <label className="block">
        <span className={labelClassName}>{translate("Match mode")}</span>
        <select
          className={inputClassName}
          name={`${idPrefix}MatchMode`}
          onChange={(event) =>
            onChange({
              ...draft,
              matchMode: event.target.value as TargetingRuleDraft["matchMode"],
            })
          }
          value={draft.matchMode}
        >
          <option value={TargetingMatchModeObject.TARGETING_MATCH_MODE_ALL}>
            {translate("All conditions")}</option>
          <option value={TargetingMatchModeObject.TARGETING_MATCH_MODE_ANY}>
            {translate("Any condition")}</option>
        </select>
      </label>

      <datalist id={`${idPrefix}-attribute-options`}>
        {catalog?.attributes.map((attribute) => (
          <option key={attribute.key} value={attribute.key}>
            {attribute.displayName}
          </option>
        ))}
      </datalist>

      <div className="space-y-3">
        {draft.conditions.map((condition, index) => {
          const operatorDefinitions = catalog?.operators.filter((metadata) =>
            metadata.supportedValueKinds.includes(condition.valueKind),
          );
          const hasValues = !existsOperators.has(condition.operator);
          const allowsMultiple = multipleValueOperators.has(condition.operator);
          return (
            <div
              className="rounded-xl border border-white/8 bg-white/[0.025] p-4"
              data-testid={`${idPrefix}-condition-${index}`}
              key={`${condition.id}-${index}`}
            >
              <div className="mb-3 flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-300">
                  {translate("Condition")} {index + 1}
                </span>
                <Button
                  aria-label={translate(`Remove condition ${index + 1}`)}
                  disabled={draft.conditions.length === 1}
                  onClick={() => removeCondition(index)}
                  size="sm"
                  type="button"
                  variant="ghost"
                >
                  <Trash2 aria-hidden="true" className="size-3.5" />
                  {translate("Remove")}</Button>
              </div>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <label className="block">
                  <span className={labelClassName}>{translate("Condition ID")}</span>
                  <input
                    className={inputClassName}
                    name={`${idPrefix}ConditionId`}
                    onChange={(event) =>
                      updateCondition(index, { id: event.target.value })
                    }
                    value={condition.id}
                  />
                </label>
                <label className="block">
                  <span className={labelClassName}>{translate("Attribute")}</span>
                  <input
                    className={inputClassName}
                    list={`${idPrefix}-attribute-options`}
                    name={`${idPrefix}ConditionAttribute`}
                    onChange={(event) =>
                      updateCondition(index, { attribute: event.target.value })
                    }
                    placeholder={translate("region or subscription.plan")}
                    value={condition.attribute}
                  />
                </label>
                <label className="block">
                  <span className={labelClassName}>{translate("Value type")}</span>
                  <select
                    className={inputClassName}
                    name={`${idPrefix}ConditionValueKind`}
                    onChange={(event) =>
                      changeValueKind(index, event.target.value as ValueKind)
                    }
                    value={condition.valueKind}
                  >
                    <option value={TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT}>
                      {translate("Text")}</option>
                    <option value={TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH}>
                      {translate("Boolean")}</option>
                    <option value={TargetingValueKindObject.TARGETING_VALUE_KIND_NUMERIC}>
                      {translate("Number")}</option>
                  </select>
                </label>
                <label className="block">
                  <span className={labelClassName}>{translate("Operator")}</span>
                  <select
                    className={inputClassName}
                    name={`${idPrefix}ConditionOperator`}
                    onChange={(event) =>
                      updateCondition(index, {
                        operator: event.target.value as Operator,
                        rawValues: "",
                      })
                    }
                    value={condition.operator}
                  >
                    {operatorDefinitions?.map((metadata) => (
                      <option key={metadata.operator} value={metadata.operator}>
                        {metadata.displayName}
                      </option>
                    )) ?? (
                      <option value={TargetingOperatorObject.TARGETING_OPERATOR_EQUALS}>
                        {translate("Equals")}</option>
                    )}
                  </select>
                </label>
              </div>
              <div className="mt-3 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                <label className="block">
                  <span className={labelClassName}>{translate("Comparison value")}</span>
                  <input
                    className={inputClassName}
                    disabled={!hasValues}
                    name={`${idPrefix}ConditionValue`}
                    onChange={(event) =>
                      updateCondition(index, { rawValues: event.target.value })
                    }
                    placeholder={
                      translate(hasValues
                        ? allowsMultiple
                          ? "Comma-separated values"
                          : condition.valueKind ===
                              TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH
                            ? "true or false"
                            : "Comparison value"
                        : "No value required")
                    }
                    value={condition.rawValues}
                  />
                </label>
                <label className="flex h-10 items-center gap-2 text-xs text-slate-400">
                  <input
                    checked={condition.caseSensitive}
                    disabled={
                      condition.valueKind !==
                      TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT
                    }
                    name={`${idPrefix}ConditionCaseSensitive`}
                    onChange={(event) =>
                      updateCondition(index, { caseSensitive: event.target.checked })
                    }
                    type="checkbox"
                  />
                  {translate("Case sensitive")}</label>
              </div>
            </div>
          );
        })}
      </div>

      <Button
        disabled={draft.conditions.length >= (catalog?.maximumConditions ?? 50)}
        onClick={() =>
          onChange({
            ...draft,
            conditions: [
              ...draft.conditions,
              createConditionDraft(draft.conditions.length + 1),
            ],
          })
        }
        size="sm"
        type="button"
        variant="outline"
      >
        <Plus aria-hidden="true" className="size-3.5" />
        {translate("Add condition")}</Button>
    </div>
  );
}

function createConditionDraft(index: number): TargetingConditionDraft {
  return {
    attribute: "region",
    caseSensitive: false,
    id: `condition-${index}`,
    operator: TargetingOperatorObject.TARGETING_OPERATOR_EQUALS,
    rawValues: "",
    valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT,
  };
}

function parseValues(condition: TargetingConditionDraft): TargetingValueInput[] {
  if (existsOperators.has(condition.operator)) {
    return [];
  }

  const rawValues = multipleValueOperators.has(condition.operator)
    ? condition.rawValues.split(",").map((value) => value.trim()).filter(Boolean)
    : [condition.rawValues.trim()];
  return rawValues.map((value) => {
    switch (condition.valueKind) {
      case TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT:
        return { text: value };
      case TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH:
        if (value.toLowerCase() !== "true" && value.toLowerCase() !== "false") {
          throw new Error(`Condition ${condition.id} requires true or false.`);
        }
        return { truth: value.toLowerCase() === "true" };
      case TargetingValueKindObject.TARGETING_VALUE_KIND_NUMERIC: {
        const numeric = Number(value);
        if (!Number.isFinite(numeric) || value.length === 0) {
          throw new Error(`Condition ${condition.id} requires a finite number.`);
        }
        return { numeric };
      }
    }
  });
}

function formatValue(value: TargetingValueInput): string {
  if ("text" in value) {
    return value.text;
  }
  if ("truth" in value) {
    return String(value.truth);
  }
  return String(value.numeric);
}
