import { create } from "zustand";

type AnalyticsSelectionState = {
  applicationId: string;
  environmentId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectEnvironment: (environmentId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useAnalyticsSelection = create<AnalyticsSelectionState>((set) => ({
  applicationId: "",
  environmentId: "",
  tenantId: "",
  selectApplication: (applicationId) => set({ applicationId, environmentId: "" }),
  selectEnvironment: (environmentId) => set({ environmentId }),
  selectTenant: (tenantId) =>
    set({ applicationId: "", environmentId: "", tenantId }),
}));
