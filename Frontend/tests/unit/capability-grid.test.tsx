import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import {
  CapabilityGrid,
  type CapabilityView,
} from "@/features/platform/platform-overview";

const capabilities: CapabilityView[] = [
  {
    key: "rpc",
    displayName: "RPC and HTTP",
    lifecycle: "CAPABILITY_LIFECYCLE_AVAILABLE",
  },
  {
    key: "identity",
    displayName: "Identity",
    lifecycle: "CAPABILITY_LIFECYCLE_PLANNED",
  },
];

describe("CapabilityGrid", () => {
  it("renders live and planned lifecycle states", () => {
    render(<CapabilityGrid capabilities={capabilities} />);

    expect(screen.getByTestId("capability-rpc")).toHaveTextContent("Ready");
    expect(screen.getByTestId("capability-identity")).toHaveTextContent(
      "Planned",
    );
    expect(screen.getByText("RPC and HTTP")).toBeInTheDocument();
    expect(screen.getByText("Identity")).toBeInTheDocument();
  });
});
