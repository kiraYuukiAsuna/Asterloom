import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

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
});
