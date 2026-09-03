"use client";

import {
  Archive,
  Box,
  ChevronLeft,
  ChevronRight,
  Copy,
  Download,
  FileBox,
  FolderCog,
  HardDriveUpload,
  LoaderCircle,
  Pencil,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Trash2,
  Upload,
} from "lucide-react";
import Link from "next/link";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SearchableSelect } from "@/components/ui/searchable-select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  StorageAccessPolicyObject,
  StorageObjectStatusObject,
  StorageResourceStatusObject,
} from "@/lib/api/generated/models";
import {
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import {
  archiveStorageBucket,
  completeStorageUpload,
  copyStorageObject,
  createStorageBucket,
  createStorageDownloadUrl,
  createStorageUploadSession,
  deleteStorageObject,
  downloadStorageTransfer,
  formatStorageMetadata,
  getStorageBucket,
  getStorageObject,
  listStorageBuckets,
  listStorageObjects,
  parseStorageMetadata,
  restoreStorageBucket,
  sha256Hex,
  storageErrorMessage,
  updateStorageBucket,
  updateStorageObjectMetadata,
  uploadStorageTransfer,
  type StorageAccessPolicy,
  type StorageBucketRecord,
  type StorageObjectRecord,
  type StorageUploadSessionRecord,
} from "@/lib/api/storage-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { useStorageSelection } from "./storage-store";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const textAreaClassName = cn(inputClassName, "h-24 resize-y py-2.5");
const labelClassName = "grid gap-1.5 text-xs font-medium text-slate-400";
const emptyPage = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function StorageWorkspace({
  csrfToken,
  view,
}: {
  csrfToken: string;
  view: "buckets" | "objects";
}) {
  const hydrated = useHydrated();
  const selection = useStorageSelection();
  const tenantsQuery = useSWR(hydrated ? ["storage-tenants"] : null, () =>
    listTenants(emptyPage),
  );
  const tenants = tenantsQuery.data?.tenants ?? [];
  const tenantId = tenants.some((tenant) => tenant.id === selection.tenantId)
    ? selection.tenantId
    : (tenants[0]?.id ?? "");

  return (
    <div
      className="space-y-6"
      data-hydrated={hydrated ? "true" : "false"}
      data-storage-workspace
    >
      <section className="theme-hero-sky flex flex-col gap-5 rounded-2xl border border-sky-400/15 bg-gradient-to-br from-sky-400/[0.08] via-slate-950/60 to-indigo-400/[0.04] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">{translate("Verified object plane")}</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">
            {translate("Storage control center")}</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            {translate("Govern quotas and content policies, then transfer bytes with short-lived, least-privilege tickets and SHA-256 verification.")}</p>
        </div>
        <nav aria-label={translate("Storage views")} className="flex rounded-xl border border-white/10 p-1">
          <Link
            className={tabClassName(view === "buckets")}
            href="/storage/buckets"
          >
            {translate("Buckets")}</Link>
          <Link
            className={tabClassName(view === "objects")}
            href="/storage/objects"
          >
            {translate("Objects")}</Link>
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{translate("Tenant boundary")}</CardTitle>
            <CardDescription>
              {translate("Every bucket and object is isolated under one tenant.")}</CardDescription>
          </div>
          <Button
            aria-label={translate("Refresh tenant list")}
            disabled={tenantsQuery.isLoading}
            onClick={() => void tenantsQuery.mutate()}
            size="sm"
            type="button"
            variant="outline"
          >
            <RefreshCw aria-hidden="true" className="size-3.5" />
            {translate("Refresh")}</Button>
        </CardHeader>
        <CardContent>
          <SearchableSelect
            ariaLabel={translate("Storage tenant")}
            className={inputClassName}
            disabled={tenantsQuery.isLoading || tenants.length === 0}
            emptyLabel={translate("No active tenant")}
            label={translate("Storage tenant")}
            labelClassName={labelClassName}
            onChange={selection.selectTenant}
            options={tenants.map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))}
            value={tenantId}
          />
          {tenantsQuery.error && (
            <p className="mt-3 text-sm text-rose-300">
              {translate(storageErrorMessage(tenantsQuery.error))}
            </p>
          )}
        </CardContent>
      </Card>

      {!tenantId ? (
        <EmptyState message={translate("Create an active tenant before provisioning storage.")} />
      ) : view === "buckets" ? (
        <BucketWorkspace csrfToken={csrfToken} tenantId={tenantId} />
      ) : (
        <ObjectWorkspace csrfToken={csrfToken} tenantId={tenantId} />
      )}
    </div>
  );
}

function BucketWorkspace({ csrfToken, tenantId }: { csrfToken: string; tenantId: string }) {
  const selection = useStorageSelection();
  const [includeArchived, setIncludeArchived] = useState(false);
  const [query, setQuery] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [pageToken, setPageToken] = useState("");
  const [previousTokens, setPreviousTokens] = useState<string[]>([]);
  const [detail, setDetail] = useState<StorageBucketRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const bucketsQuery = useSWR(
    ["storage-buckets", tenantId, includeArchived, query, pageToken],
    () => listStorageBuckets(tenantId, { includeArchived, pageSize: 25, pageToken, query }),
    { keepPreviousData: true },
  );
  const buckets = bucketsQuery.data?.buckets ?? [];
  const effectiveBucketId = buckets.some((bucket) => bucket.id === selection.bucketId)
    ? selection.bucketId
    : (buckets[0]?.id ?? "");
  const selectedBucket =
    detail?.id === effectiveBucketId
      ? detail
      : (buckets.find((bucket) => bucket.id === effectiveBucketId) ?? null);

  function applyFilter(event: FormEvent) {
    event.preventDefault();
    setQuery(queryDraft.trim());
    setPageToken("");
    setPreviousTokens([]);
  }

  async function loadBucket(bucket: StorageBucketRecord) {
    setGettingId(bucket.id);
    try {
      const loaded = await getStorageBucket(tenantId, bucket.id);
      selection.selectBucket(loaded.id);
      setDetail(loaded);
      toast.success(translate("Bucket details refreshed."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  async function refresh(updated?: StorageBucketRecord) {
    if (updated) {
      selection.selectBucket(updated.id);
      setDetail(updated);
    }
    await bucketsQuery.mutate();
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
      <div className="space-y-6">
        <Card data-ui-action="list-storage-buckets">
          <CardHeader>
            <CardTitle>{translate("Bucket inventory")}</CardTitle>
            <CardDescription>
              {translate("Search active or archived buckets and inspect their live usage.")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <form className="flex flex-col gap-3 sm:flex-row" onSubmit={applyFilter}>
              <label className="relative flex-1">
                <span className="sr-only">{translate("Search storage buckets")}</span>
                <Search
                  aria-hidden="true"
                  className="absolute left-3 top-3 size-4 text-slate-600"
                />
                <input
                  className={cn(inputClassName, "pl-9")}
                  onChange={(event) => setQueryDraft(event.target.value)}
                  placeholder={translate("Search key, name, or description")}
                  value={queryDraft}
                />
              </label>
              <Button type="submit" variant="outline">
                {translate("Apply")}</Button>
            </form>
            <label className="flex items-center gap-2 text-xs text-slate-400">
              <input
                aria-label={translate("Include archived storage buckets")}
                checked={includeArchived}
                onChange={(event) => {
                  setIncludeArchived(event.target.checked);
                  setPageToken("");
                  setPreviousTokens([]);
                }}
                type="checkbox"
              />
              {translate("Include archived buckets")}</label>

            {bucketsQuery.isLoading ? (
              <LoadingState label={translate("Loading buckets")} />
            ) : bucketsQuery.error ? (
              <ErrorState error={bucketsQuery.error} />
            ) : buckets.length === 0 ? (
              <EmptyState message={translate("No buckets match this view.")} compact />
            ) : (
              <div className="space-y-2">
                {buckets.map((bucket) => (
                  <article
                    className={cn(
                      "rounded-xl border p-4 transition",
                      bucket.id === effectiveBucketId
                        ? "border-sky-400/30 bg-sky-400/[0.06]"
                        : "border-white/8 bg-white/[0.02] hover:border-white/15",
                    )}
                    data-testid={`storage-bucket-${bucket.key}`}
                    key={bucket.id}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <button
                        className="min-w-0 text-left"
                        onClick={() => {
                          selection.selectBucket(bucket.id);
                          setDetail(null);
                        }}
                        type="button"
                      >
                        <span className="flex items-center gap-2">
                          <span className="truncate text-sm font-medium text-white">
                            {bucket.displayName}
                          </span>
                          <ResourceStatusBadge status={bucket.status} />
                        </span>
                        <span className="mt-1 block font-mono text-xs text-sky-300">
                          {bucket.key}
                        </span>
                        <span className="mt-2 block text-xs text-slate-500">
                          {formatBytes(bucket.usedBytes)} / {formatBytes(bucket.quotaBytes)} ·{" "}
                          {bucket.objectCount} {translate("objects")}</span>
                      </button>
                      <Button
                        data-ui-action="get-storage-bucket"
                        disabled={gettingId === bucket.id}
                        onClick={() => void loadBucket(bucket)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {gettingId === bucket.id ? (
                          <LoaderCircle aria-hidden="true" className="size-3.5 animate-spin" />
                        ) : (
                          <FolderCog aria-hidden="true" className="size-3.5" />
                        )}
                        {translate("Inspect")}</Button>
                    </div>
                  </article>
                ))}
              </div>
            )}

            <Pagination
              canGoNext={Boolean(bucketsQuery.data?.nextPageToken)}
              canGoPrevious={previousTokens.length > 0}
              onNext={() => {
                setPreviousTokens((tokens) => [...tokens, pageToken]);
                setPageToken(bucketsQuery.data?.nextPageToken ?? "");
              }}
              onPrevious={() => {
                const prior = previousTokens.at(-1) ?? "";
                setPreviousTokens((tokens) => tokens.slice(0, -1));
                setPageToken(prior);
              }}
            />
          </CardContent>
        </Card>

        <CreateBucketPanel csrfToken={csrfToken} onCreated={refresh} tenantId={tenantId} />
      </div>

      {selectedBucket ? (
        <BucketInspector
          bucket={selectedBucket}
          csrfToken={csrfToken}
          key={`${selectedBucket.id}:${selectedBucket.version}`}
          onChanged={refresh}
        />
      ) : (
        <EmptyState message={translate("Select a bucket to edit policy and lifecycle settings.")} />
      )}
    </div>
  );
}

function CreateBucketPanel({
  csrfToken,
  onCreated,
  tenantId,
}: {
  csrfToken: string;
  onCreated: (bucket: StorageBucketRecord) => Promise<void>;
  tenantId: string;
}) {
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [quotaMiB, setQuotaMiB] = useState("1024");
  const [maxObjectMiB, setMaxObjectMiB] = useState("256");
  const [contentTypes, setContentTypes] = useState("*/*");
  const [accessPolicy, setAccessPolicy] = useState<StorageAccessPolicy>(
    StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_PRIVATE,
  );

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const bucket = await createStorageBucket(csrfToken, tenantId, {
        accessPolicy,
        allowedContentTypes: splitContentTypes(contentTypes),
        description,
        displayName,
        key,
        maxObjectSizeBytes: mebibytes(maxObjectMiB),
        quotaBytes: mebibytes(quotaMiB),
      });
      await onCreated(bucket);
      setKey("");
      setDisplayName("");
      setDescription("");
      toast.success(translate("Storage bucket created."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Create bucket")}</CardTitle>
        <CardDescription>
          {translate("Define quota, maximum object size, accepted media types, and read policy together.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <label className={labelClassName}>
            {translate("Bucket key")}<input
              className={inputClassName}
              name="bucketKey"
              onChange={(event) => setKey(event.target.value)}
              placeholder={translate("release-artifacts")}
              required
              value={key}
            />
          </label>
          <label className={labelClassName}>
            {translate("Display name")}<input
              className={inputClassName}
              name="bucketDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder={translate("Release artifacts")}
              required
              value={displayName}
            />
          </label>
          <label className={cn(labelClassName, "sm:col-span-2")}>
            {translate("Description")}<textarea
              className={textAreaClassName}
              name="bucketDescription"
              onChange={(event) => setDescription(event.target.value)}
              value={description}
            />
          </label>
          <label className={labelClassName}>
            {translate("Quota (MiB)")}<input
              className={inputClassName}
              min="1"
              name="bucketQuotaMiB"
              onChange={(event) => setQuotaMiB(event.target.value)}
              required
              type="number"
              value={quotaMiB}
            />
          </label>
          <label className={labelClassName}>
            {translate("Maximum object (MiB)")}<input
              className={inputClassName}
              min="1"
              name="bucketMaxObjectMiB"
              onChange={(event) => setMaxObjectMiB(event.target.value)}
              required
              type="number"
              value={maxObjectMiB}
            />
          </label>
          <label className={labelClassName}>
            {translate("Allowed content types")}<input
              className={inputClassName}
              name="bucketContentTypes"
              onChange={(event) => setContentTypes(event.target.value)}
              placeholder={translate("image/*, application/json")}
              value={contentTypes}
            />
          </label>
          <label className={labelClassName}>
            {translate("Access policy")}<select
              aria-label={translate("Create bucket access policy")}
              className={inputClassName}
              name="bucketAccessPolicy"
              onChange={(event) => setAccessPolicy(event.target.value as StorageAccessPolicy)}
              value={accessPolicy}
            >
              <option value={StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_PRIVATE}>
                {translate("Private")}</option>
              <option
                value={
                  StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_AUTHENTICATED_READ
                }
              >
                {translate("Authenticated read")}</option>
            </select>
          </label>
          <div className="sm:col-span-2">
            <Button
              data-ui-action="create-storage-bucket"
              disabled={busy}
              type="submit"
            >
              {busy ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Plus aria-hidden="true" className="size-4" />
              )}
              {translate("Create bucket")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function BucketInspector({
  bucket,
  csrfToken,
  onChanged,
}: {
  bucket: StorageBucketRecord;
  csrfToken: string;
  onChanged: (bucket: StorageBucketRecord) => Promise<void>;
}) {
  const [busy, setBusy] = useState("");
  const [displayName, setDisplayName] = useState(bucket.displayName);
  const [description, setDescription] = useState(bucket.description);
  const [quotaMiB, setQuotaMiB] = useState(toMebibytes(bucket.quotaBytes));
  const [maxObjectMiB, setMaxObjectMiB] = useState(
    toMebibytes(bucket.maxObjectSizeBytes),
  );
  const [contentTypes, setContentTypes] = useState(bucket.allowedContentTypes.join(", "));
  const [accessPolicy, setAccessPolicy] = useState<StorageAccessPolicy>(bucket.accessPolicy);
  const archived =
    bucket.status === StorageResourceStatusObject.STORAGE_RESOURCE_STATUS_ARCHIVED;

  async function update(event: FormEvent) {
    event.preventDefault();
    setBusy("update");
    try {
      const updated = await updateStorageBucket(csrfToken, bucket, {
        accessPolicy,
        allowedContentTypes: splitContentTypes(contentTypes),
        description,
        displayName,
        maxObjectSizeBytes: mebibytes(maxObjectMiB),
        quotaBytes: mebibytes(quotaMiB),
      });
      await onChanged(updated);
      toast.success(translate("Bucket policy updated."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function changeLifecycle(action: "archive" | "restore") {
    if (action === "archive" && !window.confirm(translate("Archive this empty bucket?"))) return;
    setBusy(action);
    try {
      const updated =
        action === "archive"
          ? await archiveStorageBucket(csrfToken, bucket)
          : await restoreStorageBucket(csrfToken, bucket);
      await onChanged(updated);
      toast.success(translate(action === "archive" ? "Bucket archived." : "Bucket restored."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>{translate("Bucket policy")}</CardTitle>
          <ResourceStatusBadge status={bucket.status} />
        </div>
        <CardDescription>
          <span className="font-mono text-sky-300">{bucket.key}</span> {" "}{translate("· version")}{" "}{bucket.version}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <dl className="grid grid-cols-2 gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-xs">
          <Metric label={translate("Used")} value={formatBytes(bucket.usedBytes)} />
          <Metric label={translate("Objects")} value={bucket.objectCount.toString()} />
          <Metric label={translate("Quota")} value={formatBytes(bucket.quotaBytes)} />
          <Metric label={translate("Max object")} value={formatBytes(bucket.maxObjectSizeBytes)} />
        </dl>
        <form className="space-y-4" onSubmit={update}>
          <label className={labelClassName}>
            {translate("Display name")}<input
              className={inputClassName}
              disabled={archived}
              name="editBucketDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              value={displayName}
            />
          </label>
          <label className={labelClassName}>
            {translate("Description")}<textarea
              className={textAreaClassName}
              disabled={archived}
              name="editBucketDescription"
              onChange={(event) => setDescription(event.target.value)}
              value={description}
            />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label className={labelClassName}>
              {translate("Quota MiB")}<input
                className={inputClassName}
                disabled={archived}
                min="1"
                name="editBucketQuotaMiB"
                onChange={(event) => setQuotaMiB(event.target.value)}
                type="number"
                value={quotaMiB}
              />
            </label>
            <label className={labelClassName}>
              {translate("Max object MiB")}<input
                className={inputClassName}
                disabled={archived}
                min="1"
                name="editBucketMaxObjectMiB"
                onChange={(event) => setMaxObjectMiB(event.target.value)}
                type="number"
                value={maxObjectMiB}
              />
            </label>
          </div>
          <label className={labelClassName}>
            {translate("Allowed content types")}<input
              className={inputClassName}
              disabled={archived}
              name="editBucketContentTypes"
              onChange={(event) => setContentTypes(event.target.value)}
              value={contentTypes}
            />
          </label>
          <label className={labelClassName}>
            {translate("Access policy")}<select
              aria-label={translate("Edit bucket access policy")}
              className={inputClassName}
              disabled={archived}
              name="editBucketAccessPolicy"
              onChange={(event) => setAccessPolicy(event.target.value as StorageAccessPolicy)}
              value={accessPolicy}
            >
              <option value={StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_PRIVATE}>
                {translate("Private")}</option>
              <option
                value={
                  StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_AUTHENTICATED_READ
                }
              >
                {translate("Authenticated read")}</option>
            </select>
          </label>
          {!archived && (
            <Button
              className="w-full"
              data-ui-action="update-storage-bucket"
              disabled={Boolean(busy)}
              type="submit"
            >
              {busy === "update" ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Pencil aria-hidden="true" className="size-4" />
              )}
              {translate("Update bucket")}</Button>
          )}
        </form>
        {archived ? (
          <Button
            className="w-full"
            data-ui-action="restore-storage-bucket"
            disabled={Boolean(busy)}
            onClick={() => void changeLifecycle("restore")}
            type="button"
            variant="outline"
          >
            <RotateCcw aria-hidden="true" className="size-4" />
            {translate("Restore bucket")}</Button>
        ) : (
          <Button
            className="w-full"
            data-ui-action="archive-storage-bucket"
            disabled={Boolean(busy) || bucket.objectCount > 0}
            onClick={() => void changeLifecycle("archive")}
            title={bucket.objectCount > 0 ? "Delete every object before archiving." : undefined}
            type="button"
            variant="outline"
          >
            <Archive aria-hidden="true" className="size-4" />
            {translate("Archive empty bucket")}</Button>
        )}
      </CardContent>
    </Card>
  );
}

function ObjectWorkspace({ csrfToken, tenantId }: { csrfToken: string; tenantId: string }) {
  const selection = useStorageSelection();
  const [includeDeleted, setIncludeDeleted] = useState(false);
  const [query, setQuery] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [pageToken, setPageToken] = useState("");
  const [previousTokens, setPreviousTokens] = useState<string[]>([]);
  const [detail, setDetail] = useState<StorageObjectRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const bucketsQuery = useSWR(
    ["storage-object-buckets", tenantId],
    () => listAllActiveBuckets(tenantId),
    { dedupingInterval: 0, revalidateOnMount: true },
  );
  const buckets = bucketsQuery.data ?? [];
  const bucketId = buckets.some((bucket) => bucket.id === selection.bucketId)
    ? selection.bucketId
    : (buckets[0]?.id ?? "");
  const objectsQuery = useSWR(
    bucketId
      ? ["storage-objects", tenantId, bucketId, includeDeleted, query, pageToken]
      : null,
    () =>
      listStorageObjects(tenantId, bucketId, {
        includeDeleted,
        pageSize: 25,
        pageToken,
        query,
      }),
    { keepPreviousData: true },
  );
  const objects = objectsQuery.data?.objects ?? [];
  const effectiveObjectId = objects.some((item) => item.id === selection.objectId)
    ? selection.objectId
    : (objects[0]?.id ?? "");
  const selectedObject =
    detail?.id === effectiveObjectId
      ? detail
      : (objects.find((item) => item.id === effectiveObjectId) ?? null);

  async function loadObject(storageObject: StorageObjectRecord) {
    setGettingId(storageObject.id);
    try {
      const loaded = await getStorageObject(tenantId, bucketId, storageObject.id);
      selection.selectObject(loaded.id);
      setDetail(loaded);
      toast.success(translate("Object details refreshed."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  async function refreshObject(updated?: StorageObjectRecord) {
    if (updated) {
      if (updated.bucketId !== bucketId) selection.selectBucket(updated.bucketId);
      selection.selectObject(updated.id);
      setDetail(updated);
    }
    await Promise.all([objectsQuery.mutate(), bucketsQuery.mutate()]);
  }

  function applyFilter(event: FormEvent) {
    event.preventDefault();
    setQuery(queryDraft.trim());
    setPageToken("");
    setPreviousTokens([]);
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>{translate("Object scope")}</CardTitle>
          <CardDescription>
            {translate("Choose the active bucket that receives uploads and object operations.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <label className={labelClassName}>
            {translate("Object bucket")}<select
              aria-label={translate("Object bucket")}
              className={inputClassName}
              disabled={bucketsQuery.isLoading || buckets.length === 0}
              onChange={(event) => {
                selection.selectBucket(event.target.value);
                setDetail(null);
                setPageToken("");
                setPreviousTokens([]);
              }}
              value={bucketId}
            >
              {buckets.length === 0 && <option value="">{translate("No active bucket")}</option>}
              {buckets.map((bucket) => (
                <option key={bucket.id} value={bucket.id}>
                  {bucket.displayName} ({bucket.key})
                </option>
              ))}
            </select>
          </label>
          {bucketsQuery.error && <ErrorState error={bucketsQuery.error} />}
        </CardContent>
      </Card>

      {!bucketId ? (
        <EmptyState message={translate("Create an active bucket before uploading objects.")} />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
          <div className="space-y-6">
            <UploadPanel
              bucketId={bucketId}
              csrfToken={csrfToken}
              onCompleted={refreshObject}
              tenantId={tenantId}
            />

            <Card data-ui-action="list-storage-objects">
              <CardHeader>
                <CardTitle>{translate("Object inventory")}</CardTitle>
                <CardDescription>
                  {translate("Inspect integrity metadata and include tombstones when auditing deletions.")}</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <form className="flex flex-col gap-3 sm:flex-row" onSubmit={applyFilter}>
                  <label className="relative flex-1">
                    <span className="sr-only">{translate("Search storage objects")}</span>
                    <Search
                      aria-hidden="true"
                      className="absolute left-3 top-3 size-4 text-slate-600"
                    />
                    <input
                      className={cn(inputClassName, "pl-9")}
                      onChange={(event) => setQueryDraft(event.target.value)}
                      placeholder={translate("Search object key or file name")}
                      value={queryDraft}
                    />
                  </label>
                  <Button type="submit" variant="outline">
                    {translate("Apply")}</Button>
                </form>
                <label className="flex items-center gap-2 text-xs text-slate-400">
                  <input
                    aria-label={translate("Include deleted storage objects")}
                    checked={includeDeleted}
                    onChange={(event) => {
                      setIncludeDeleted(event.target.checked);
                      setPageToken("");
                      setPreviousTokens([]);
                    }}
                    type="checkbox"
                  />
                  {translate("Include deleted objects")}</label>

                {objectsQuery.isLoading ? (
                  <LoadingState label={translate("Loading objects")} />
                ) : objectsQuery.error ? (
                  <ErrorState error={objectsQuery.error} />
                ) : objects.length === 0 ? (
                  <EmptyState message={translate("No objects match this view.")} compact />
                ) : (
                  <div className="space-y-2">
                    {objects.map((storageObject) => (
                      <article
                        className={cn(
                          "rounded-xl border p-4 transition",
                          storageObject.id === effectiveObjectId
                            ? "border-sky-400/30 bg-sky-400/[0.06]"
                            : "border-white/8 bg-white/[0.02] hover:border-white/15",
                        )}
                        data-testid={`storage-object-${storageObject.objectKey}`}
                        key={storageObject.id}
                      >
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                          <button
                            className="min-w-0 text-left"
                            onClick={() => {
                              selection.selectObject(storageObject.id);
                              setDetail(null);
                            }}
                            type="button"
                          >
                            <span className="flex items-center gap-2">
                              <span className="truncate text-sm font-medium text-white">
                                {storageObject.fileName}
                              </span>
                              <ObjectStatusBadge status={storageObject.status} />
                            </span>
                            <span className="mt-1 block break-all font-mono text-xs text-sky-300">
                              {storageObject.objectKey}
                            </span>
                            <span className="mt-2 block text-xs text-slate-500">
                              {formatBytes(storageObject.sizeBytes)} · {storageObject.contentType} {translate("· v")} {storageObject.version}
                            </span>
                          </button>
                          <Button
                            data-ui-action="get-storage-object"
                            disabled={gettingId === storageObject.id}
                            onClick={() => void loadObject(storageObject)}
                            size="sm"
                            type="button"
                            variant="outline"
                          >
                            {gettingId === storageObject.id ? (
                              <LoaderCircle aria-hidden="true" className="size-3.5 animate-spin" />
                            ) : (
                              <FileBox aria-hidden="true" className="size-3.5" />
                            )}
                            {translate("Inspect")}</Button>
                        </div>
                      </article>
                    ))}
                  </div>
                )}

                <Pagination
                  canGoNext={Boolean(objectsQuery.data?.nextPageToken)}
                  canGoPrevious={previousTokens.length > 0}
                  onNext={() => {
                    setPreviousTokens((tokens) => [...tokens, pageToken]);
                    setPageToken(objectsQuery.data?.nextPageToken ?? "");
                  }}
                  onPrevious={() => {
                    const prior = previousTokens.at(-1) ?? "";
                    setPreviousTokens((tokens) => tokens.slice(0, -1));
                    setPageToken(prior);
                  }}
                />
              </CardContent>
            </Card>
          </div>

          {selectedObject ? (
            <ObjectInspector
              buckets={buckets}
              csrfToken={csrfToken}
              key={`${selectedObject.id}:${selectedObject.version}`}
              onChanged={refreshObject}
              storageObject={selectedObject}
            />
          ) : (
            <EmptyState message={translate("Select an object to manage metadata and lifecycle.")} />
          )}
        </div>
      )}
    </div>
  );
}

function UploadPanel({
  bucketId,
  csrfToken,
  onCompleted,
  tenantId,
}: {
  bucketId: string;
  csrfToken: string;
  onCompleted: (storageObject: StorageObjectRecord) => Promise<void>;
  tenantId: string;
}) {
  const selection = useStorageSelection();
  const applicationsQuery = useSWR(["storage-applications", tenantId], () =>
    listApplications(tenantId, emptyPage),
  );
  const applications = applicationsQuery.data?.applications ?? [];
  const applicationId = applications.some((item) => item.id === selection.applicationId)
    ? selection.applicationId
    : "";
  const environmentsQuery = useSWR(
    applicationId ? ["storage-environments", tenantId, applicationId] : null,
    () => listEnvironments(tenantId, applicationId, emptyPage),
  );
  const environments = environmentsQuery.data?.environments ?? [];
  const environmentId = environments.some((item) => item.id === selection.environmentId)
    ? selection.environmentId
    : "";
  const [file, setFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [objectKey, setObjectKey] = useState("");
  const [contentType, setContentType] = useState("application/octet-stream");
  const [metadata, setMetadata] = useState("");
  const [pending, setPending] = useState<{
    file: File;
    session: StorageUploadSessionRecord;
  } | null>(null);
  const [busy, setBusy] = useState("");

  async function createSession(event: FormEvent) {
    event.preventDefault();
    if (!file) {
      toast.error(translate("Choose a non-empty file first."));
      return;
    }
    setBusy("session");
    try {
      const session = await createStorageUploadSession(csrfToken, tenantId, bucketId, {
        applicationId: applicationId || undefined,
        contentType,
        customMetadata: parseStorageMetadata(metadata),
        environmentId: environmentId || undefined,
        fileName: file.name,
        objectKey,
        sha256: await sha256Hex(file),
        sizeBytes: file.size,
      });
      setPending({ file, session });
      toast.success(translate("Upload session created. Transfer it before the ticket expires."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function transferAndComplete() {
    if (!pending) return;
    setBusy("complete");
    try {
      await uploadStorageTransfer(csrfToken, pending.session.transfer, pending.file);
      const completed = await completeStorageUpload(csrfToken, pending.session);
      await onCompleted(completed);
      setPending(null);
      setFile(null);
      setFileInputKey((value) => value + 1);
      setObjectKey("");
      setMetadata("");
      toast.success(translate("Object uploaded and verified."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Verified upload")}</CardTitle>
        <CardDescription>
          {translate("Phase 1 reserves quota and issues a PUT ticket. Phase 2 transfers bytes and verifies size, content type, and SHA-256 before publishing the object.")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={createSession}>
          <label className={cn(labelClassName, "sm:col-span-2")}>
            {translate("File")}<input
              className={cn(inputClassName, "py-2")}
              key={fileInputKey}
              name="storageFile"
              onChange={(event) => {
                const next = event.target.files?.[0] ?? null;
                setFile(next);
                if (next) {
                  setObjectKey((current) => current || safeObjectKey(next.name));
                  setContentType(next.type || "application/octet-stream");
                }
              }}
              required
              type="file"
            />
          </label>
          <label className={labelClassName}>
            {translate("Object key")}<input
              className={inputClassName}
              name="storageObjectKey"
              onChange={(event) => setObjectKey(event.target.value)}
              placeholder={translate("uploads/example.json")}
              required
              value={objectKey}
            />
          </label>
          <label className={labelClassName}>
            {translate("Content type")}<input
              className={inputClassName}
              name="storageContentType"
              onChange={(event) => setContentType(event.target.value)}
              required
              value={contentType}
            />
          </label>
          <SearchableSelect
            ariaLabel={translate("Upload application")}
            className={inputClassName}
            emptyLabel={translate("Tenant-level object")}
            label={translate("Upload application (optional)")}
            labelClassName={labelClassName}
            onChange={selection.selectApplication}
            options={applications.map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))}
            value={applicationId}
          />
          <SearchableSelect
            ariaLabel={translate("Upload environment")}
            className={inputClassName}
            disabled={!applicationId}
            emptyLabel={translate("Application-level object")}
            label={translate("Upload environment (optional)")}
            labelClassName={labelClassName}
            onChange={selection.selectEnvironment}
            options={environments.map((environment) => ({ label: `${environment.displayName} (${environment.slug})`, value: environment.id }))}
            value={environmentId}
          />
          <label className={cn(labelClassName, "sm:col-span-2")}>
            {translate("Custom metadata (one key=value per line)")}<textarea
              className={textAreaClassName}
              name="uploadMetadata"
              onChange={(event) => setMetadata(event.target.value)}
              placeholder={translate("source=console\nretention=standard")}
              value={metadata}
            />
          </label>
          <div className="sm:col-span-2">
            <Button
              data-ui-action="create-storage-upload-session"
              disabled={Boolean(busy) || Boolean(pending)}
              type="submit"
            >
              {busy === "session" ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <HardDriveUpload aria-hidden="true" className="size-4" />
              )}
              {translate("Create upload session")}</Button>
          </div>
        </form>

        {pending && (
          <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.06] p-4">
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <p className="text-sm font-medium text-amber-200">{translate("Transfer ticket ready")}</p>
                <p className="mt-1 truncate font-mono text-xs text-slate-500">
                  {pending.session.id}
                </p>
                <p className="mt-1 text-xs text-slate-500">
                  {translate("Expires")} {formatDate(pending.session.expiresAt)}
                </p>
              </div>
              <Button
                data-ui-action="complete-storage-upload"
                disabled={Boolean(busy)}
                onClick={() => void transferAndComplete()}
                type="button"
              >
                {busy === "complete" ? (
                  <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                ) : (
                  <Upload aria-hidden="true" className="size-4" />
                )}
                {translate("Upload bytes & complete")}</Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ObjectInspector({
  buckets,
  csrfToken,
  onChanged,
  storageObject,
}: {
  buckets: StorageBucketRecord[];
  csrfToken: string;
  onChanged: (storageObject: StorageObjectRecord) => Promise<void>;
  storageObject: StorageObjectRecord;
}) {
  const [busy, setBusy] = useState("");
  const [fileName, setFileName] = useState(storageObject.fileName);
  const [metadata, setMetadata] = useState(
    formatStorageMetadata(storageObject.customMetadata),
  );
  const [targetBucketId, setTargetBucketId] = useState(storageObject.bucketId);
  const [copyKey, setCopyKey] = useState(`${storageObject.objectKey}.copy`);
  const [copyFileName, setCopyFileName] = useState(`copy-${storageObject.fileName}`);
  const [copyMetadata, setCopyMetadata] = useState(
    formatStorageMetadata(storageObject.customMetadata),
  );
  const available =
    storageObject.status === StorageObjectStatusObject.STORAGE_OBJECT_STATUS_AVAILABLE;

  async function updateMetadata(event: FormEvent) {
    event.preventDefault();
    setBusy("metadata");
    try {
      const updated = await updateStorageObjectMetadata(
        csrfToken,
        storageObject,
        fileName,
        parseStorageMetadata(metadata),
      );
      await onChanged(updated);
      toast.success(translate("Object metadata updated."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function download() {
    setBusy("download");
    try {
      const ticket = await createStorageDownloadUrl(csrfToken, storageObject);
      const blob = await downloadStorageTransfer(ticket, storageObject);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = storageObject.fileName;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1_000);
      toast.success(translate("Download verified with SHA-256."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function copy(event: FormEvent) {
    event.preventDefault();
    setBusy("copy");
    try {
      const copied = await copyStorageObject(csrfToken, storageObject, {
        customMetadata: parseStorageMetadata(copyMetadata),
        fileName: copyFileName,
        objectKey: copyKey,
        targetBucketId,
      });
      await onChanged(copied);
      toast.success(translate("Object copied."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function remove() {
    if (!window.confirm(translate("Delete this object and its stored bytes?"))) return;
    setBusy("delete");
    try {
      const deleted = await deleteStorageObject(csrfToken, storageObject);
      await onChanged(deleted);
      toast.success(translate("Object deleted."));
    } catch (error) {
      toast.error(translate(storageErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>{translate("Object operations")}</CardTitle>
          <ObjectStatusBadge status={storageObject.status} />
        </div>
        <CardDescription>
          <span className="break-all font-mono text-sky-300">
            {storageObject.objectKey}
          </span>
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <dl className="grid grid-cols-2 gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-xs">
          <Metric label={translate("Size")} value={formatBytes(storageObject.sizeBytes)} />
          <Metric label={translate("Version")} value={storageObject.version.toString()} />
          <Metric label={translate("Media type")} value={storageObject.contentType} />
          <Metric label={translate("Completed")} value={formatDate(storageObject.completedAt)} />
        </dl>
        <div>
          <p className="text-xs font-medium text-slate-400">{translate("SHA-256")}</p>
          <p className="mt-1 break-all rounded-lg bg-black/25 p-2 font-mono text-[10px] leading-4 text-emerald-300">
            {storageObject.sha256}
          </p>
        </div>

        <form className="space-y-3" onSubmit={updateMetadata}>
          <h3 className="text-sm font-semibold text-white">{translate("Metadata")}</h3>
          <label className={labelClassName}>
            {translate("File name")}<input
              className={inputClassName}
              disabled={!available}
              name="editObjectFileName"
              onChange={(event) => setFileName(event.target.value)}
              value={fileName}
            />
          </label>
          <label className={labelClassName}>
            {translate("Custom metadata")}<textarea
              className={textAreaClassName}
              disabled={!available}
              name="editObjectMetadata"
              onChange={(event) => setMetadata(event.target.value)}
              value={metadata}
            />
          </label>
          <Button
            className="w-full"
            data-ui-action="update-storage-object-metadata"
            disabled={Boolean(busy) || !available}
            type="submit"
            variant="outline"
          >
            <Pencil aria-hidden="true" className="size-4" />
            {translate("Update metadata")}</Button>
        </form>

        <Button
          className="w-full"
          data-ui-action="create-storage-download-url"
          disabled={Boolean(busy) || !available}
          onClick={() => void download()}
          type="button"
        >
          {busy === "download" ? (
            <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
          ) : (
            <Download aria-hidden="true" className="size-4" />
          )}
          {translate("Create ticket & download")}</Button>

        <form className="space-y-3 border-t border-white/8 pt-5" onSubmit={copy}>
          <h3 className="text-sm font-semibold text-white">{translate("Copy object")}</h3>
          <label className={labelClassName}>
            {translate("Target bucket")}<select
              className={inputClassName}
              disabled={!available}
              name="copyTargetBucket"
              onChange={(event) => setTargetBucketId(event.target.value)}
              value={targetBucketId}
            >
              {buckets.map((bucket) => (
                <option key={bucket.id} value={bucket.id}>
                  {bucket.displayName} ({bucket.key})
                </option>
              ))}
            </select>
          </label>
          <label className={labelClassName}>
            {translate("New object key")}<input
              className={inputClassName}
              disabled={!available}
              name="copyObjectKey"
              onChange={(event) => setCopyKey(event.target.value)}
              value={copyKey}
            />
          </label>
          <label className={labelClassName}>
            {translate("New file name")}<input
              className={inputClassName}
              disabled={!available}
              name="copyObjectFileName"
              onChange={(event) => setCopyFileName(event.target.value)}
              value={copyFileName}
            />
          </label>
          <label className={labelClassName}>
            {translate("Copy metadata")}<textarea
              className={textAreaClassName}
              disabled={!available}
              name="copyObjectMetadata"
              onChange={(event) => setCopyMetadata(event.target.value)}
              value={copyMetadata}
            />
          </label>
          <Button
            className="w-full"
            data-ui-action="copy-storage-object"
            disabled={Boolean(busy) || !available}
            type="submit"
            variant="outline"
          >
            <Copy aria-hidden="true" className="size-4" />
            {translate("Copy object")}</Button>
        </form>

        <Button
          className="w-full"
          data-ui-action="delete-storage-object"
          disabled={Boolean(busy) || !available}
          onClick={() => void remove()}
          type="button"
          variant="outline"
        >
          <Trash2 aria-hidden="true" className="size-4" />
          {translate("Delete object")}</Button>
      </CardContent>
    </Card>
  );
}

function Pagination({
  canGoNext,
  canGoPrevious,
  onNext,
  onPrevious,
}: {
  canGoNext: boolean;
  canGoPrevious: boolean;
  onNext: () => void;
  onPrevious: () => void;
}) {
  if (!canGoNext && !canGoPrevious) return null;
  return (
    <div className="flex justify-end gap-2 border-t border-white/8 pt-4">
      <Button disabled={!canGoPrevious} onClick={onPrevious} size="sm" type="button" variant="outline">
        <ChevronLeft aria-hidden="true" className="size-3.5" /> {translate("Previous")}</Button>
      <Button disabled={!canGoNext} onClick={onNext} size="sm" type="button" variant="outline">
        {translate("Next")}<ChevronRight aria-hidden="true" className="size-3.5" />
      </Button>
    </div>
  );
}

function ResourceStatusBadge({ status }: { status: StorageBucketRecord["status"] }) {
  const active = status === StorageResourceStatusObject.STORAGE_RESOURCE_STATUS_ACTIVE;
  return <Badge variant={active ? "success" : "planned"}>{translate(active ? "Active" : "Archived")}</Badge>;
}

function ObjectStatusBadge({ status }: { status: StorageObjectRecord["status"] }) {
  const available = status === StorageObjectStatusObject.STORAGE_OBJECT_STATUS_AVAILABLE;
  const deleted = status === StorageObjectStatusObject.STORAGE_OBJECT_STATUS_DELETED;
  return (
    <Badge variant={available ? "success" : deleted ? "planned" : "info"}>
      {translate(status.replace("STORAGE_OBJECT_STATUS_", "").toLowerCase())}
    </Badge>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-slate-500">{label}</dt>
      <dd className="mt-1 break-words font-medium text-slate-200">{value}</dd>
    </div>
  );
}

function LoadingState({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-center gap-2 rounded-xl border border-white/8 py-10 text-sm text-slate-500">
      <LoaderCircle aria-hidden="true" className="size-4 animate-spin" /> {label}
    </div>
  );
}

function ErrorState({ error }: { error: unknown }) {
  return (
    <p className="rounded-xl border border-rose-400/15 bg-rose-400/[0.05] p-4 text-sm text-rose-300">
      {translate(storageErrorMessage(error))}
    </p>
  );
}

function EmptyState({ message, compact = false }: { message: string; compact?: boolean }) {
  return (
    <div
      className={cn(
        "grid place-items-center rounded-2xl border border-dashed border-white/10 bg-white/[0.015] text-center text-sm text-slate-500",
        compact ? "py-10" : "min-h-52 p-8",
      )}
    >
      <div>
        <Box aria-hidden="true" className="mx-auto mb-3 size-5 text-slate-600" />
        {message}
      </div>
    </div>
  );
}

function tabClassName(active: boolean) {
  return cn(
    "rounded-lg px-4 py-2 text-sm font-medium transition",
    active ? "bg-sky-400 text-slate-950" : "text-slate-400 hover:bg-white/[0.06] hover:text-white",
  );
}

function splitContentTypes(value: string) {
  return value
    .split(",")
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);
}

function mebibytes(value: string) {
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) throw new Error("Enter a positive whole MiB value.");
  return parsed * 1_048_576;
}

function toMebibytes(bytes: number) {
  return Math.max(1, Math.round(bytes / 1_048_576)).toString();
}

function formatBytes(bytes: number) {
  if (bytes < 1_024) return `${bytes} B`;
  const units = ["KiB", "MiB", "GiB", "TiB"];
  let value = bytes / 1_024;
  let unit = units[0];
  for (let index = 1; index < units.length && value >= 1_024; index += 1) {
    value /= 1_024;
    unit = units[index];
  }
  return `${value >= 10 ? value.toFixed(0) : value.toFixed(1)} ${unit}`;
}

function formatDate(value: string | null | undefined) {
  return value ? formatDateTime(value) : "—";
}

function safeObjectKey(fileName: string) {
  const normalized = fileName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "-")
    .replace(/^[^a-z0-9]+|[^a-z0-9]+$/g, "");
  return `uploads/${normalized || "object"}`;
}

async function listAllActiveBuckets(tenantId: string) {
  const buckets: StorageBucketRecord[] = [];
  const visitedTokens = new Set<string>();
  let pageToken = "";
  do {
    if (visitedTokens.has(pageToken)) {
      throw new Error("The Storage API returned a repeated bucket page token.");
    }
    visitedTokens.add(pageToken);
    const page = await listStorageBuckets(tenantId, {
      includeArchived: false,
      pageSize: 100,
      pageToken,
      query: "",
    });
    buckets.push(...page.buckets);
    pageToken = page.nextPageToken;
  } while (pageToken);
  return buckets;
}
