import { create } from "zustand";

type FeatureSelectionState = {
  applicationId: string;
  environmentId: string;
  flagId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectEnvironment: (environmentId: string) => void;
  selectFlag: (flagId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useFeatureSelection = create<FeatureSelectionState>((set) => ({
  applicationId: "",
  environmentId: "",
  flagId: "",
  tenantId: "",
  selectApplication: (applicationId) =>
    set({ applicationId, environmentId: "", flagId: "" }),
  selectEnvironment: (environmentId) => set({ environmentId, flagId: "" }),
  selectFlag: (flagId) => set({ flagId }),
  selectTenant: (tenantId) =>
    set({ tenantId, applicationId: "", environmentId: "", flagId: "" }),
}));
