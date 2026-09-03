import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import {
  createFeatureDefinitionDraft,
  FeatureDefinitionEditor,
  type FeatureDefinitionDraft,
} from "@/features/feature/definition-editor";
import { FeatureValueKindObject } from "@/lib/api/generated/models";

describe("FeatureDefinitionEditor prerequisite selectors", () => {
  it("starts a prerequisite with a published flag and one of its variants", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [draft, setDraft] = useState<FeatureDefinitionDraft>(() =>
        createFeatureDefinitionDraft(FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN),
      );
      return (
        <FeatureDefinitionEditor
          draft={draft}
          idPrefix="create"
          onChange={setDraft}
          prerequisiteFlags={[
            {
              displayName: "Checkout enabled",
              key: "checkout-enabled",
              variants: [
                { displayName: "Disabled", key: "off" },
                { displayName: "Enabled", key: "on" },
              ],
            },
          ]}
          segments={[]}
          valueKind={FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN}
        />
      );
    }

    render(<Harness />);
    await user.click(screen.getByRole("button", { name: "Add prerequisite" }));

    expect(screen.getByRole("combobox", { name: "create prerequisite 1 flag" })).toHaveValue(
      "Checkout enabled (checkout-enabled)",
    );
    expect(
      screen.getByRole("combobox", { name: "create prerequisite 1 expected variant" }),
    ).toHaveValue("Disabled (off)");
  });
});
