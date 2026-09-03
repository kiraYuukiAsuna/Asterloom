import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { useState } from "react";

import { SearchableMultiSelect } from "@/components/ui/searchable-multi-select";
import { SearchableSelect } from "@/components/ui/searchable-select";

describe("SearchableSelect", () => {
  it("resolves a late-loaded label to its identifier", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <SearchableSelect
        ariaLabel="Tenant"
        className=""
        emptyLabel="Choose a tenant"
        onChange={onChange}
        options={[]}
        value=""
      />,
    );

    await user.type(screen.getByRole("combobox", { name: "Tenant" }), "Beta");
    await user.tab();
    rerender(
      <SearchableSelect
        ariaLabel="Tenant"
        className=""
        emptyLabel="Choose a tenant"
        onChange={onChange}
        options={[
          { label: "Alpha", value: "tenant-alpha" },
          { label: "Beta", value: "tenant-beta" },
        ]}
        value=""
      />,
    );

    await waitFor(() => expect(onChange).toHaveBeenCalledWith("tenant-beta"));
  });

  it("adds and removes only catalog options in a multi-select", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [value, setValue] = useState<string[]>([]);
      return (
        <SearchableMultiSelect
          ariaLabel="Add permission"
          className=""
          emptyLabel="Select a permission"
          label="Permissions"
          onChange={setValue}
          options={[
            { label: "Read flags (feature.flag.read)", value: "feature.flag.read" },
            { label: "Publish flags (feature.flag.publish)", value: "feature.flag.publish" },
          ]}
          value={value}
        />
      );
    }

    render(<Harness />);
    await user.type(
      screen.getByRole("combobox", { name: "Add permission" }),
      "Read flags (feature.flag.read)",
    );
    expect(screen.getByText("Read flags (feature.flag.read)")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remove Read flags (feature.flag.read)" }));
    expect(screen.queryByText("Read flags (feature.flag.read)")).not.toBeInTheDocument();
  });
});
