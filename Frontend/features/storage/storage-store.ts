import { create } from "zustand";

type StorageSelectionState = {
  applicationId: string;
  bucketId: string;
  environmentId: string;
  objectId: string;
  tenantId: string;
  selectApplication: (applicationId: string) => void;
  selectBucket: (bucketId: string) => void;
  selectEnvironment: (environmentId: string) => void;
  selectObject: (objectId: string) => void;
  selectTenant: (tenantId: string) => void;
};

export const useStorageSelection = create<StorageSelectionState>((set) => ({
  applicationId: "",
  bucketId: "",
  environmentId: "",
  objectId: "",
  tenantId: "",
  selectApplication: (applicationId) =>
    set({ applicationId, environmentId: "" }),
  selectBucket: (bucketId) => set({ bucketId, objectId: "" }),
  selectEnvironment: (environmentId) => set({ environmentId }),
  selectObject: (objectId) => set({ objectId }),
  selectTenant: (tenantId) =>
    set({
      applicationId: "",
      bucketId: "",
      environmentId: "",
      objectId: "",
      tenantId,
    }),
}));
