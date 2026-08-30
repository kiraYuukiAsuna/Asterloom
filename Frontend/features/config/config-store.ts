import { create } from "zustand";

type ConfigSelectionState = {
  applicationId: string;
  entryId: string;
  environmentId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectEntry: (entryId: string) => void;
  selectEnvironment: (environmentId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useConfigSelection = create<ConfigSelectionState>((set) => ({
  applicationId: "",
  entryId: "",
  environmentId: "",
  tenantId: "",
  selectApplication: (applicationId) =>
    set({ applicationId, entryId: "", environmentId: "" }),
  selectEntry: (entryId) => set({ entryId }),
  selectEnvironment: (environmentId) => set({ entryId: "", environmentId }),
  selectTenant: (tenantId) =>
    set({ applicationId: "", entryId: "", environmentId: "", tenantId }),
}));
