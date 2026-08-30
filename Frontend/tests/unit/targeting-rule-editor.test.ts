import { describe, expect, it } from "vitest";

import {
  TargetingMatchModeObject,
  TargetingOperatorObject,
  TargetingValueKindObject,
} from "@/lib/api/generated/models";
import {
  createRuleDraft,
  toRuleInput,
  type TargetingRuleDraft,
} from "@/features/targeting/rule-editor";

describe("Targeting rule editor conversion", () => {
  it("starts with a valid deterministic text condition", () => {
    const draft = createRuleDraft();

    expect(draft).toEqual({
      matchMode: TargetingMatchModeObject.TARGETING_MATCH_MODE_ALL,
      conditions: [
        {
          attribute: "region",
          caseSensitive: false,
          id: "condition-1",
          operator: TargetingOperatorObject.TARGETING_OPERATOR_EQUALS,
          rawValues: "",
          valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT,
        },
      ],
    });
  });

  it("converts text lists, booleans, numbers, and existence checks to typed API values", () => {
    const draft: TargetingRuleDraft = {
      matchMode: TargetingMatchModeObject.TARGETING_MATCH_MODE_ANY,
      conditions: [
        {
          attribute: "region",
          caseSensitive: true,
          id: "region-list",
          operator: TargetingOperatorObject.TARGETING_OPERATOR_ONE_OF,
          rawValues: "cn, us,  sg ",
          valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT,
        },
        {
          attribute: "beta",
          caseSensitive: false,
          id: "beta-enabled",
          operator: TargetingOperatorObject.TARGETING_OPERATOR_EQUALS,
          rawValues: "TRUE",
          valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH,
        },
        {
          attribute: "score",
          caseSensitive: false,
          id: "score-threshold",
          operator: TargetingOperatorObject.TARGETING_OPERATOR_GREATER_THAN,
          rawValues: "42.5",
          valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_NUMERIC,
        },
        {
          attribute: "subscription.plan",
          caseSensitive: false,
          id: "plan-present",
          operator: TargetingOperatorObject.TARGETING_OPERATOR_EXISTS,
          rawValues: "ignored",
          valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TEXT,
        },
      ],
    };

    expect(toRuleInput(draft)).toEqual({
      matchMode: TargetingMatchModeObject.TARGETING_MATCH_MODE_ANY,
      conditions: [
        expect.objectContaining({
          id: "region-list",
          values: [{ text: "cn" }, { text: "us" }, { text: "sg" }],
        }),
        expect.objectContaining({
          id: "beta-enabled",
          values: [{ truth: true }],
        }),
        expect.objectContaining({
          id: "score-threshold",
          values: [{ numeric: 42.5 }],
        }),
        expect.objectContaining({ id: "plan-present", values: [] }),
      ],
    });
  });

  it("rejects invalid typed values before an API request is sent", () => {
    const draft = createRuleDraft();
    draft.conditions[0] = {
      ...draft.conditions[0],
      id: "boolean-condition",
      rawValues: "sometimes",
      valueKind: TargetingValueKindObject.TARGETING_VALUE_KIND_TRUTH,
    };

    expect(() => toRuleInput(draft)).toThrow(
      "Condition boolean-condition requires true or false.",
    );
  });
});
