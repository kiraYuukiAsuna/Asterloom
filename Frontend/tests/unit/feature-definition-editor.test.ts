import { describe, expect, it } from "vitest";

import {
  createFeatureDefinitionDraft,
  parseFeatureValue,
  toFeatureDefinitionInput,
} from "@/features/feature/definition-editor";
import { FeatureValueKindObject } from "@/lib/api/generated/models";

describe("feature definition editor", () => {
  it("parses every supported typed feature value without string coercion", () => {
    expect(
      parseFeatureValue(
        "false",
        FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
      ),
    ).toEqual({ booleanValue: false });
    expect(
      parseFeatureValue("42", FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER),
    ).toEqual({ integerValue: 42 });
    expect(
      parseFeatureValue("2.5", FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE),
    ).toEqual({ doubleValue: 2.5 });
    expect(
      parseFeatureValue(
        '{"layout":"compact"}',
        FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT,
      ),
    ).toEqual({ objectJson: '{"layout":"compact"}' });
  });

  it("rejects invalid typed values and overlapping allocation ranges", () => {
    expect(() =>
      parseFeatureValue(
        "sometimes",
        FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
      ),
    ).toThrow(/true or false/);

    const draft = createFeatureDefinitionDraft(
      FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
    );
    draft.allocations[1] = {
      ...draft.allocations[1],
      start: "40000",
    };
    expect(() =>
      toFeatureDefinitionInput(
        draft,
        FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
      ),
    ).toThrow(/overlap/i);
  });

  it("round-trips the default Boolean draft into a complete definition", () => {
    const definition = toFeatureDefinitionInput(
      createFeatureDefinitionDraft(
        FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
      ),
      FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
    );

    expect(definition.defaultVariantKey).toBe("off");
    expect(definition.variants).toHaveLength(2);
    expect(definition.allocations).toEqual([
      { variantKey: "off", start: 0, end: 50_000 },
      { variantKey: "on", start: 50_000, end: 100_000 },
    ]);
  });
});
