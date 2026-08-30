import { create } from "zustand";

type TargetingSelectionState = {
  applicationId: string;
  environmentId: string;
  segmentId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectEnvironment: (environmentId: string) => void;
  selectSegment: (segmentId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useTargetingSelection = create<TargetingSelectionState>((set) => ({
  applicationId: "",
  environmentId: "",
  segmentId: "",
  tenantId: "",
  selectApplication: (applicationId) =>
    set({ applicationId, environmentId: "", segmentId: "" }),
  selectEnvironment: (environmentId) => set({ environmentId, segmentId: "" }),
  selectSegment: (segmentId) => set({ segmentId }),
  selectTenant: (tenantId) =>
    set({ tenantId, applicationId: "", environmentId: "", segmentId: "" }),
}));
