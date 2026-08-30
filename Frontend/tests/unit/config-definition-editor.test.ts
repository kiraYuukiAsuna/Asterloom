import { describe, expect, it } from "vitest";

import {
  createConfigDefinitionDraft,
  parseConfigValue,
  toConfigDefinitionInput,
} from "@/features/config/definition-editor";
import { ConfigValueKindObject } from "@/lib/api/generated/models";

describe("configuration definition editor", () => {
  it("parses all supported values without losing their declared type", () => {
    expect(
      parseConfigValue("false", ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN),
    ).toEqual({ booleanValue: false });
    expect(
      parseConfigValue("42", ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER),
    ).toEqual({ integerValue: 42 });
    expect(
      parseConfigValue("2.5", ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE),
    ).toEqual({ doubleValue: 2.5 });
    expect(
      parseConfigValue("hello", ConfigValueKindObject.CONFIG_VALUE_KIND_STRING),
    ).toEqual({ stringValue: "hello" });
    expect(
      parseConfigValue(
        '{"layout":"compact"}',
        ConfigValueKindObject.CONFIG_VALUE_KIND_JSON,
      ),
    ).toEqual({ jsonValue: '{"layout":"compact"}' });
  });

  it("rejects invalid primitive and JSON values", () => {
    expect(() =>
      parseConfigValue("sometimes", ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN),
    ).toThrow(/true or false/);
    expect(() =>
      parseConfigValue("1.2", ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER),
    ).toThrow(/whole numbers/);

    const draft = createConfigDefinitionDraft(
      ConfigValueKindObject.CONFIG_VALUE_KIND_JSON,
    );
    draft.defaultRawValue = "[]";
    expect(() =>
      toConfigDefinitionInput(draft, ConfigValueKindObject.CONFIG_VALUE_KIND_JSON),
    ).toThrow(/JSON object/);
  });

  it("round-trips a targeting override and JSON Schema", () => {
    const draft = createConfigDefinitionDraft(
      ConfigValueKindObject.CONFIG_VALUE_KIND_STRING,
    );
    draft.defaultRawValue = "stable";
    draft.targetingRules.push({
      id: "early-access",
      rawValue: "preview",
      segmentId: "01992b5c-f18b-7cce-92a3-06acf85aad24",
    });
    const definition = toConfigDefinitionInput(
      draft,
      ConfigValueKindObject.CONFIG_VALUE_KIND_STRING,
    );
    expect(definition.defaultValue).toEqual({ stringValue: "stable" });
    expect(definition.targetingRules[0]?.value).toEqual({ stringValue: "preview" });
    expect(JSON.parse(definition.schemaJson)).toEqual({ type: "string" });
  });
});
