import { create } from "zustand";

type MailSelectionState = {
  applicationId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useMailSelection = create<MailSelectionState>((set) => ({
  applicationId: "",
  tenantId: "",
  selectApplication: (applicationId) => set({ applicationId }),
  selectTenant: (tenantId) => set({ applicationId: "", tenantId }),
}));
