"use client";

import {
  Archive,
  Braces,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  DatabaseZap,
  Diff,
  Eye,
  History,
  LoaderCircle,
  Plus,
  RefreshCw,
  RotateCcw,
  Save,
  Search,
  Send,
  ServerCog,
} from "lucide-react";
import { type FormEvent, useMemo, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  ConfigResourceStatusObject,
  ConfigValidationSeverityObject,
  ConfigValueKindObject,
  ConfigVisibilityObject,
} from "@/lib/api/generated/models";
import {
  archiveConfigEntry,
  checkConfigUpdates,
  configErrorMessage,
  createConfigEntry,
  diffConfigDraft,
  getConfigEntry,
  getConfigSnapshot,
  isConfigVersionConflict,
  listConfigEntries,
  listConfigRevisions,
  listConfigSnapshots,
  previewConfigValue,
  publishConfigEntry,
  restoreConfigEntry,
  rollbackConfigEntry,
  updateConfigDraft,
  validateConfigDraft,
  type ConfigContextInput,
  type ConfigDiffResult,
  type ConfigEffectiveValueRecord,
  type ConfigEntryRecord,
  type ConfigScope,
  type ConfigSnapshotRecord,
  type ConfigValidationResult,
  type ConfigValueInput,
  type ConfigValueKind,
  type ConfigVisibility,
} from "@/lib/api/config-management";
import {
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import { listSegments } from "@/lib/api/targeting-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import {
  ConfigDefinitionEditor,
  createConfigDefinitionDraft,
  toConfigDefinitionInput,
  type ConfigDefinitionDraft,
} from "./definition-editor";
import { useConfigSelection } from "./config-store";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const activeStatus = ConfigResourceStatusObject.CONFIG_RESOURCE_STATUS_ACTIVE;

export function ConfigWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const tenantId = useConfigSelection((state) => state.tenantId);
  const applicationId = useConfigSelection((state) => state.applicationId);
  const environmentId = useConfigSelection((state) => state.environmentId);
  const selectTenant = useConfigSelection((state) => state.selectTenant);
  const selectApplication = useConfigSelection((state) => state.selectApplication);
  const selectEnvironment = useConfigSelection((state) => state.selectEnvironment);
  const selectEntry = useConfigSelection((state) => state.selectEntry);

  const [pending, setPending] = useState("");
  const [query, setQuery] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [pageTokens, setPageTokens] = useState([""]);
  const [pageIndex, setPageIndex] = useState(0);
  const [selectedEntry, setSelectedEntry] = useState<ConfigEntryRecord>();

  const [createKey, setCreateKey] = useState("");
  const [createName, setCreateName] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [createKind, setCreateKind] = useState<ConfigValueKind>(
    ConfigValueKindObject.CONFIG_VALUE_KIND_STRING,
  );
  const [createVisibility, setCreateVisibility] = useState<ConfigVisibility>(
    ConfigVisibilityObject.CONFIG_VISIBILITY_CLIENT,
  );
  const [createDefinition, setCreateDefinition] = useState<ConfigDefinitionDraft>(() =>
    createConfigDefinitionDraft(ConfigValueKindObject.CONFIG_VALUE_KIND_STRING),
  );
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editVisibility, setEditVisibility] = useState<ConfigVisibility>(
    ConfigVisibilityObject.CONFIG_VISIBILITY_CLIENT,
  );
  const [editDefinition, setEditDefinition] = useState<ConfigDefinitionDraft>();
  const [validation, setValidation] = useState<ConfigValidationResult>();
  const [draftDiff, setDraftDiff] = useState<ConfigDiffResult>();

  const [targetingKey, setTargetingKey] = useState("");
  const [userId, setUserId] = useState("");
  const [clientVersion, setClientVersion] = useState("");
  const [region, setRegion] = useState("");
  const [useDraft, setUseDraft] = useState(false);
  const [preview, setPreview] = useState<ConfigEffectiveValueRecord>();
  const [runtimeSnapshot, setRuntimeSnapshot] = useState<ConfigSnapshotRecord>();
  const [snapshotMode, setSnapshotMode] = useState<"client" | "server">("client");
  const [knownSnapshotVersion, setKnownSnapshotVersion] = useState("0");
  const [updateResult, setUpdateResult] = useState<{
    changed: boolean;
    currentSnapshotVersion: number;
    etag: string;
  }>();

  const tenants = useSWR("config-scope-tenants", () =>
    listTenants({ includeArchived: false, pageSize: 100, pageToken: "", query: "" }),
  );
  const applications = useSWR(
    tenantId ? ["config-scope-applications", tenantId] : null,
    () =>
      listApplications(tenantId, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const environments = useSWR(
    tenantId && applicationId
      ? ["config-scope-environments", tenantId, applicationId]
      : null,
    () =>
      listEnvironments(tenantId, applicationId, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const scope = useMemo<ConfigScope | undefined>(
    () =>
      tenantId && applicationId && environmentId
        ? { tenantId, applicationId, environmentId }
        : undefined,
    [applicationId, environmentId, tenantId],
  );
  const entries = useSWR(
    scope
      ? [
          "config-entries",
          scope.tenantId,
          scope.applicationId,
          scope.environmentId,
          query,
          includeArchived,
          pageTokens[pageIndex],
        ]
      : null,
    () =>
      listConfigEntries(scope!, {
        includeArchived,
        pageSize: 25,
        pageToken: pageTokens[pageIndex],
        query,
      }),
    { keepPreviousData: true },
  );
  const segments = useSWR(
    scope
      ? ["config-active-segments", scope.tenantId, scope.applicationId, scope.environmentId]
      : null,
    () =>
      listSegments(scope!, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const revisions = useSWR(
    selectedEntry
      ? ["config-revisions", selectedEntry.id, selectedEntry.publishedRevision]
      : null,
    () => listConfigRevisions(selectedEntry!, 100),
  );
  const snapshots = useSWR(
    scope ? ["config-snapshots", scope.tenantId, scope.applicationId, scope.environmentId] : null,
    () => listConfigSnapshots(scope!, 100),
  );

  const activeSegments = (segments.data?.segments ?? []).filter((segment) =>
    segment.status.endsWith("_ACTIVE"),
  );
  const selectedTenant = tenants.data?.tenants.find((item) => item.id === tenantId);
  const selectedApplication = applications.data?.applications.find(
    (item) => item.id === applicationId,
  );
  const selectedEnvironment = environments.data?.environments.find(
    (item) => item.id === environmentId,
  );

  function resetCollectionState() {
    setPageTokens([""]);
    setPageIndex(0);
    setSelectedEntry(undefined);
    setEditDefinition(undefined);
    setValidation(undefined);
    setDraftDiff(undefined);
    setPreview(undefined);
    setRuntimeSnapshot(undefined);
    setUpdateResult(undefined);
    selectEntry("");
  }

  function changeFilter(value: string, archived = includeArchived) {
    setQuery(value);
    setIncludeArchived(archived);
    resetCollectionState();
  }

  function loadIntoEditor(entry: ConfigEntryRecord) {
    setSelectedEntry(entry);
    selectEntry(entry.id);
    setEditName(entry.displayName);
    setEditDescription(entry.description);
    setEditVisibility(entry.visibility);
    setEditDefinition(createConfigDefinitionDraft(entry.valueKind, entry.draftDefinition));
    setValidation(undefined);
    setDraftDiff(undefined);
    setPreview(undefined);
  }

  async function runMutation<T>(
    key: string,
    work: () => Promise<T>,
    successMessage: string,
  ): Promise<T | undefined> {
    setPending(key);
    try {
      const result = await work();
      await Promise.all([entries.mutate(), revisions.mutate(), snapshots.mutate()]);
      toast.success(successMessage);
      return result;
    } catch (error) {
      await entries.mutate();
      toast.error(
        isConfigVersionConflict(error)
          ? "This entry changed in another session. Reload it, review, and retry."
          : configErrorMessage(error),
      );
      return undefined;
    } finally {
      setPending("");
    }
  }

  async function inspectEntry(record: ConfigEntryRecord) {
    if (!scope) return;
    setPending(`get-${record.id}`);
    try {
      loadIntoEditor(await getConfigEntry(scope, record.id));
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  async function submitCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!scope) return;
    const created = await runMutation(
      "create",
      () =>
        createConfigEntry(csrfToken, scope, {
          definition: toConfigDefinitionInput(createDefinition, createKind),
          description: createDescription,
          displayName: createName,
          key: createKey,
          valueKind: createKind,
          visibility: createVisibility,
        }),
      "Configuration draft created.",
    );
    if (created) {
      setCreateKey("");
      setCreateName("");
      setCreateDescription("");
      setCreateDefinition(createConfigDefinitionDraft(createKind));
      loadIntoEditor(created);
    }
  }

  async function submitDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedEntry || !editDefinition) return;
    const updated = await runMutation(
      `update-${selectedEntry.id}`,
      () =>
        updateConfigDraft(csrfToken, selectedEntry, {
          definition: toConfigDefinitionInput(editDefinition, selectedEntry.valueKind),
          description: editDescription,
          displayName: editName,
          visibility: editVisibility,
        }),
      "Draft saved. Validate and inspect the diff before publishing.",
    );
    if (updated) loadIntoEditor(updated);
  }

  async function validateDraft() {
    if (!selectedEntry) return;
    setPending("validate");
    try {
      const result = await validateConfigDraft(csrfToken, selectedEntry);
      setValidation(result);
      toast[result.valid ? "success" : "error"](
        result.valid ? "Draft passed validation." : "Draft has blocking issues.",
      );
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  async function inspectDiff() {
    if (!selectedEntry) return;
    setPending("diff");
    try {
      setDraftDiff(await diffConfigDraft(selectedEntry));
      toast.success("Draft diff loaded.");
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  async function publishCurrent() {
    if (!selectedEntry || !window.confirm("Publish this draft as an immutable snapshot?")) return;
    const published = await runMutation(
      "publish",
      () => publishConfigEntry(csrfToken, selectedEntry),
      "Configuration published in a new environment snapshot.",
    );
    if (published) loadIntoEditor(published);
  }

  async function rollbackCurrent(revision: number) {
    if (!selectedEntry || !window.confirm(`Republish revision ${revision} as a new revision?`)) {
      return;
    }
    const rolledBack = await runMutation(
      `rollback-${revision}`,
      () => rollbackConfigEntry(csrfToken, selectedEntry, revision),
      `Revision ${revision} republished in a new snapshot.`,
    );
    if (rolledBack) loadIntoEditor(rolledBack);
  }

  async function changeStatus(record: ConfigEntryRecord, restore: boolean) {
    if (!restore && !window.confirm(`Archive ${record.displayName}?`)) return;
    const updated = await runMutation(
      `${restore ? "restore" : "archive"}-${record.id}`,
      () =>
        restore
          ? restoreConfigEntry(csrfToken, record)
          : archiveConfigEntry(csrfToken, record),
      restore ? "Configuration restored." : "Configuration archived.",
    );
    if (updated && selectedEntry?.id === updated.id) loadIntoEditor(updated);
  }

  function buildContext(): ConfigContextInput {
    return {
      attributes: [],
      clientVersion,
      language: "",
      platform: "",
      region,
      targetingKey,
      userId,
    };
  }

  async function runPreview() {
    if (!selectedEntry) return;
    setPending("preview");
    try {
      setPreview(await previewConfigValue(csrfToken, selectedEntry, buildContext(), useDraft));
      toast.success(`${useDraft ? "Draft" : "Published"} effective value resolved.`);
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  async function fetchRuntimeSnapshot(server: boolean, conditional = false) {
    if (!scope) return;
    const mode = server ? "server" : "client";
    setPending(server ? "server-snapshot" : "client-snapshot");
    try {
      const etag = conditional && snapshotMode === mode ? runtimeSnapshot?.etag ?? "" : "";
      const result = await getConfigSnapshot(
        csrfToken,
        scope,
        buildContext(),
        etag,
        server,
      );
      if (!result.notModified || !runtimeSnapshot || snapshotMode !== mode) {
        setRuntimeSnapshot(result);
      }
      setSnapshotMode(mode);
      toast.success(result.notModified ? "Snapshot is unchanged (ETag match)." : "Snapshot loaded.");
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  async function checkUpdates() {
    if (!scope) return;
    setPending("check-updates");
    try {
      const result = await checkConfigUpdates(
        csrfToken,
        scope,
        buildContext(),
        Number(knownSnapshotVersion),
      );
      setUpdateResult(result);
      toast.success(result.changed ? "A newer snapshot is available." : "Snapshot is current.");
    } catch (error) {
      toast.error(configErrorMessage(error));
    } finally {
      setPending("");
    }
  }

  return (
    <div
      className="space-y-6"
      data-config-workspace
      data-hydrated={hydrated ? "true" : "false"}
    >
      <fieldset className="contents" disabled={!hydrated}>
        <section className="overflow-hidden rounded-3xl border border-violet-300/10 bg-[radial-gradient(circle_at_top_right,rgba(139,92,246,0.16),transparent_38%),linear-gradient(135deg,rgba(91,33,182,0.10),rgba(15,23,42,0.88)_55%,rgba(2,6,23,0.97))] p-6 sm:p-8">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <Badge variant="info">
                <DatabaseZap className="size-3" /> Immutable snapshots live
              </Badge>
              <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
                Dynamic configuration
              </h1>
              <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
                Author typed values and JSON Schema, preview targeting, inspect diffs, and
                publish immutable environment snapshots. Client and server visibility are
                separate runtime surfaces with ETag-aware delivery.
              </p>
            </div>
            <div className="text-right text-xs leading-6 text-slate-500">
              <p>{selectedTenant?.displayName ?? "Choose a tenant"}</p>
              <p>{selectedApplication?.displayName ?? "Choose an application"}</p>
              <p className="text-violet-300">
                {selectedEnvironment?.displayName ?? "Choose an environment"}
              </p>
            </div>
          </div>
        </section>

        <ScopeSelector
          applicationId={applicationId}
          applications={applications.data?.applications ?? []}
          environmentId={environmentId}
          environments={environments.data?.environments ?? []}
          onApplication={(value) => {
            selectApplication(value);
            resetCollectionState();
          }}
          onEnvironment={(value) => {
            selectEnvironment(value);
            resetCollectionState();
          }}
          onTenant={(value) => {
            selectTenant(value);
            resetCollectionState();
          }}
          tenantId={tenantId}
          tenants={tenants.data?.tenants ?? []}
        />

        {!scope ? (
          <Card>
            <CardContent className="py-14 text-center text-sm text-slate-500">
              Select a tenant, application, and environment to manage configuration.
            </CardContent>
          </Card>
        ) : (
          <>
            <div className="grid gap-6 xl:grid-cols-[minmax(0,.92fr)_minmax(0,1.08fr)]">
              <Card data-ui-action="list-config-entries">
                <CardHeader>
                  <CardTitle>Configuration entries</CardTitle>
                  <CardDescription>
                    Search drafts and published entries in the selected environment.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="flex flex-col gap-3 sm:flex-row">
                    <label className="relative flex-1">
                      <Search className="pointer-events-none absolute left-3 top-3 size-4 text-slate-600" />
                      <input
                        aria-label="Search configuration entries"
                        className={`${inputClassName} pl-9`}
                        onChange={(event) => changeFilter(event.target.value)}
                        placeholder="Search key, name, or description"
                        value={query}
                      />
                    </label>
                    <label className="flex h-10 items-center gap-2 text-xs text-slate-400">
                      <input
                        aria-label="Include archived configuration"
                        checked={includeArchived}
                        onChange={(event) => changeFilter(query, event.target.checked)}
                        type="checkbox"
                      />
                      Include archived
                    </label>
                  </div>
                  <div className="space-y-2">
                    {(entries.data?.entries ?? []).map((entry) => (
                      <div
                        className={cn(
                          "rounded-xl border p-4 transition",
                          selectedEntry?.id === entry.id
                            ? "border-violet-400/35 bg-violet-400/[0.07]"
                            : "border-white/8 bg-white/[0.025]",
                        )}
                        data-testid={`config-entry-${entry.key}`}
                        key={entry.id}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <button
                            className="min-w-0 text-left"
                            data-ui-action="get-config-entry"
                            onClick={() => void inspectEntry(entry)}
                            type="button"
                          >
                            <p className="truncate text-sm font-semibold text-slate-100">
                              {entry.displayName}
                            </p>
                            <p className="mt-1 truncate font-mono text-[11px] text-violet-300">
                              {entry.key}
                            </p>
                          </button>
                          <Badge variant={entry.status === activeStatus ? "success" : "planned"}>
                            {entry.status === activeStatus ? "Active" : "Archived"}
                          </Badge>
                        </div>
                        <div className="mt-3 flex flex-wrap items-center gap-2 text-[11px] text-slate-500">
                          <span>{kindLabel(entry.valueKind)}</span>
                          <span>·</span>
                          <span>{visibilityLabel(entry.visibility)}</span>
                          <span>·</span>
                          <span>draft {entry.draftRevision}</span>
                          <span>·</span>
                          <span>published {entry.publishedRevision || "—"}</span>
                        </div>
                        <div className="mt-3 flex gap-2">
                          {entry.status === activeStatus ? (
                            <Button
                              data-ui-action="archive-config-entry"
                              onClick={() => void changeStatus(entry, false)}
                              size="sm"
                              variant="ghost"
                            >
                              <Archive className="size-3.5" /> Archive
                            </Button>
                          ) : (
                            <Button
                              data-ui-action="restore-config-entry"
                              onClick={() => void changeStatus(entry, true)}
                              size="sm"
                              variant="outline"
                            >
                              <RotateCcw className="size-3.5" /> Restore
                            </Button>
                          )}
                        </div>
                      </div>
                    ))}
                    {!entries.isLoading && (entries.data?.entries.length ?? 0) === 0 && (
                      <EmptyState text="No configuration entries match this scope and filter." />
                    )}
                  </div>
                  <div className="flex items-center justify-between">
                    <Button
                      disabled={pageIndex === 0}
                      onClick={() => setPageIndex((value) => Math.max(0, value - 1))}
                      size="sm"
                      variant="ghost"
                    >
                      <ChevronLeft className="size-3.5" /> Previous
                    </Button>
                    <span className="text-xs text-slate-600">Page {pageIndex + 1}</span>
                    <Button
                      disabled={!entries.data?.nextPageToken}
                      onClick={() => {
                        const token = entries.data?.nextPageToken;
                        if (!token) return;
                        setPageTokens((current) =>
                          current[pageIndex + 1]
                            ? current
                            : [...current.slice(0, pageIndex + 1), token],
                        );
                        setPageIndex((value) => value + 1);
                      }}
                      size="sm"
                      variant="ghost"
                    >
                      Next <ChevronRight className="size-3.5" />
                    </Button>
                  </div>
                </CardContent>
              </Card>

              <Card data-ui-action="create-config-entry">
                <CardHeader>
                  <CardTitle>Create a typed draft</CardTitle>
                  <CardDescription>
                    Values that resemble secrets are blocked from client visibility.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <form className="space-y-5" onSubmit={(event) => void submitCreate(event)}>
                    <div className="grid gap-4 sm:grid-cols-2">
                      <TextField label="Key" name="createConfigKey" onChange={setCreateKey} value={createKey} />
                      <TextField
                        label="Display name"
                        name="createConfigDisplayName"
                        onChange={setCreateName}
                        value={createName}
                      />
                      <SelectField
                        label="Value type"
                        name="createConfigValueKind"
                        onChange={(value) => {
                          const kind = value as ConfigValueKind;
                          setCreateKind(kind);
                          setCreateDefinition(createConfigDefinitionDraft(kind));
                        }}
                        options={valueKindOptions}
                        value={createKind}
                      />
                      <SelectField
                        label="Visibility"
                        name="createConfigVisibility"
                        onChange={(value) => setCreateVisibility(value as ConfigVisibility)}
                        options={visibilityOptions}
                        value={createVisibility}
                      />
                    </div>
                    <TextAreaField
                      label="Description"
                      name="createConfigDescription"
                      onChange={setCreateDescription}
                      value={createDescription}
                    />
                    <ConfigDefinitionEditor
                      draft={createDefinition}
                      idPrefix="create"
                      onChange={setCreateDefinition}
                      segments={activeSegments}
                      valueKind={createKind}
                    />
                    <Button disabled={pending === "create"} type="submit">
                      {pending === "create" ? (
                        <LoaderCircle className="size-4 animate-spin" />
                      ) : (
                        <Plus className="size-4" />
                      )}
                      Create configuration
                    </Button>
                  </form>
                </CardContent>
              </Card>
            </div>

            {selectedEntry && editDefinition && (
              <>
                <Card data-ui-action="update-config-draft">
                  <CardHeader className="border-b border-white/8">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                      <div>
                        <div className="flex flex-wrap items-center gap-2">
                          <CardTitle>{selectedEntry.displayName}</CardTitle>
                          <Badge
                            variant={
                              selectedEntry.status === activeStatus ? "success" : "planned"
                            }
                          >
                            {selectedEntry.status === activeStatus ? "Active" : "Archived"}
                          </Badge>
                        </div>
                        <CardDescription className="mt-1 font-mono text-xs text-violet-300">
                          {selectedEntry.key} · version {selectedEntry.version} · snapshot {" "}
                          {selectedEntry.publishedSnapshotVersion || "unpublished"}
                        </CardDescription>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        <Button
                          data-ui-action="validate-config-draft"
                          onClick={() => void validateDraft()}
                          size="sm"
                          type="button"
                          variant="outline"
                        >
                          <CheckCircle2 className="size-3.5" /> Validate
                        </Button>
                        <Button
                          data-ui-action="diff-config-draft"
                          onClick={() => void inspectDiff()}
                          size="sm"
                          type="button"
                          variant="outline"
                        >
                          <Diff className="size-3.5" /> Diff
                        </Button>
                        <Button
                          data-ui-action="publish-config-entry"
                          disabled={selectedEntry.status !== activeStatus}
                          onClick={() => void publishCurrent()}
                          size="sm"
                          type="button"
                        >
                          <Send className="size-3.5" /> Publish snapshot
                        </Button>
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent className="pt-6">
                    <form className="space-y-5" onSubmit={(event) => void submitDraft(event)}>
                      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                        <TextField
                          label="Display name"
                          name="editConfigDisplayName"
                          onChange={setEditName}
                          value={editName}
                        />
                        <SelectField
                          label="Visibility"
                          name="editConfigVisibility"
                          onChange={(value) => setEditVisibility(value as ConfigVisibility)}
                          options={visibilityOptions}
                          value={editVisibility}
                        />
                        <div className="rounded-lg border border-white/8 bg-white/[0.025] px-3 py-2">
                          <span className={labelClassName}>Fixed value type</span>
                          <p className="text-sm text-slate-200">
                            {kindLabel(selectedEntry.valueKind)}
                          </p>
                        </div>
                      </div>
                      <TextAreaField
                        label="Description"
                        name="editConfigDescription"
                        onChange={setEditDescription}
                        value={editDescription}
                      />
                      <ConfigDefinitionEditor
                        draft={editDefinition}
                        idPrefix="edit"
                        onChange={setEditDefinition}
                        segments={activeSegments}
                        valueKind={selectedEntry.valueKind}
                      />
                      <Button
                        disabled={selectedEntry.status !== activeStatus || pending.startsWith("update")}
                        type="submit"
                      >
                        <Save className="size-4" /> Save draft
                      </Button>
                    </form>
                  </CardContent>
                </Card>

                <div className="grid gap-6 xl:grid-cols-2">
                  <Card>
                    <CardHeader>
                      <CardTitle>Validation & diff</CardTitle>
                      <CardDescription>
                        Server-side schema, type, secret, and targeting checks are authoritative.
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="space-y-4">
                      {validation ? (
                        <div>
                          <Badge variant={validation.valid ? "success" : "planned"}>
                            {validation.valid ? "Draft is publishable" : "Draft is blocked"}
                          </Badge>
                          <p className="mt-2 break-all font-mono text-[10px] text-slate-600">
                            SHA-256 {validation.definitionHash}
                          </p>
                          <div className="mt-3 space-y-2">
                            {validation.issues.map((issue, index) => (
                              <div
                                className="rounded-lg border border-white/8 bg-white/[0.025] p-3 text-xs"
                                key={`${issue.code}-${index}`}
                              >
                                <p
                                  className={
                                    issue.severity ===
                                    ConfigValidationSeverityObject.CONFIG_VALIDATION_SEVERITY_ERROR
                                      ? "text-rose-300"
                                      : "text-amber-300"
                                  }
                                >
                                  {issue.code} · {issue.path || "definition"}
                                </p>
                                <p className="mt-1 text-slate-400">{issue.message}</p>
                              </div>
                            ))}
                          </div>
                        </div>
                      ) : (
                        <EmptyState text="Run validation to see publish blockers and warnings." />
                      )}
                      {draftDiff && (
                        <div className="space-y-3">
                          <div className="flex items-center gap-2 text-xs text-slate-300">
                            <Diff className="size-4 text-violet-300" />
                            {draftDiff.changed
                              ? `${draftDiff.changedPaths.length} changed path(s)`
                              : "Draft matches the published definition"}
                          </div>
                          <div className="flex flex-wrap gap-1.5">
                            {draftDiff.changedPaths.map((path) => (
                              <code
                                className="rounded bg-violet-400/10 px-2 py-1 text-[10px] text-violet-200"
                                key={path}
                              >
                                {path}
                              </code>
                            ))}
                          </div>
                          <details className="rounded-xl border border-white/8 bg-slate-950/70 p-3">
                            <summary className="cursor-pointer text-xs text-slate-400">
                              Compare serialized definitions
                            </summary>
                            <div className="mt-3 grid gap-3 lg:grid-cols-2">
                              <JsonBlock label="Published" value={draftDiff.publishedJson} />
                              <JsonBlock label="Draft" value={draftDiff.draftJson} />
                            </div>
                          </details>
                        </div>
                      )}
                    </CardContent>
                  </Card>

                  <Card data-ui-action="list-config-revisions">
                    <CardHeader>
                      <CardTitle>Immutable revisions</CardTitle>
                      <CardDescription>
                        Rollback republishes a historical definition as a new revision and snapshot.
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="space-y-2">
                      {(revisions.data?.revisions ?? []).map((revision) => (
                        <div
                          className="flex items-center justify-between gap-4 rounded-xl border border-white/8 bg-white/[0.025] p-4"
                          key={revision.id}
                        >
                          <div>
                            <p className="text-sm font-medium text-slate-200">
                              Revision {revision.revision}
                            </p>
                            <p className="mt-1 text-[11px] text-slate-500">
                              snapshot {revision.snapshotVersion}
                              {revision.sourceRevision
                                ? ` · from revision ${revision.sourceRevision}`
                                : ""}
                            </p>
                          </div>
                          <Button
                            data-ui-action="rollback-config-entry"
                            disabled={selectedEntry.status !== activeStatus}
                            onClick={() => void rollbackCurrent(revision.revision)}
                            size="sm"
                            variant="outline"
                          >
                            <RotateCcw className="size-3.5" /> Roll back
                          </Button>
                        </div>
                      ))}
                      {!revisions.isLoading && (revisions.data?.revisions.length ?? 0) === 0 && (
                        <EmptyState text="Publish the draft to create revision history." />
                      )}
                    </CardContent>
                  </Card>
                </div>

                <Card>
                  <CardHeader>
                    <CardTitle>Effective value lab</CardTitle>
                    <CardDescription>
                      Exercise draft preview, client/server snapshots, conditional ETag requests,
                      and update checks with one evaluation context.
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-5">
                    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                      <TextField
                        label="Targeting key"
                        name="configTargetingKey"
                        onChange={setTargetingKey}
                        value={targetingKey}
                      />
                      <TextField label="User ID" name="configUserId" onChange={setUserId} value={userId} />
                      <TextField
                        label="Client version"
                        name="configClientVersion"
                        onChange={setClientVersion}
                        value={clientVersion}
                      />
                      <TextField label="Region" name="configRegion" onChange={setRegion} value={region} />
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <label className="mr-2 flex items-center gap-2 text-xs text-slate-400">
                        <input
                          checked={useDraft}
                          onChange={(event) => setUseDraft(event.target.checked)}
                          type="checkbox"
                        />
                        Preview draft
                      </label>
                      <Button
                        data-ui-action="preview-config-value"
                        onClick={() => void runPreview()}
                        size="sm"
                        variant="outline"
                      >
                        <Eye className="size-3.5" /> Preview entry
                      </Button>
                      <Button
                        data-ui-action="get-config-snapshot"
                        onClick={() => void fetchRuntimeSnapshot(false)}
                        size="sm"
                        variant="outline"
                      >
                        <Braces className="size-3.5" /> Client snapshot
                      </Button>
                      <Button
                        data-ui-action="get-server-config-snapshot"
                        onClick={() => void fetchRuntimeSnapshot(true)}
                        size="sm"
                        variant="outline"
                      >
                        <ServerCog className="size-3.5" /> Server snapshot
                      </Button>
                      <Button
                        disabled={!runtimeSnapshot}
                        onClick={() => void fetchRuntimeSnapshot(snapshotMode === "server", true)}
                        size="sm"
                        variant="ghost"
                      >
                        <RefreshCw className="size-3.5" /> Conditional refresh
                      </Button>
                    </div>
                    <div className="grid gap-4 lg:grid-cols-2">
                      <ResultPanel title="Entry preview">
                        {preview ? (
                          <EffectiveValue value={preview} />
                        ) : (
                          <p>Preview an entry against this context.</p>
                        )}
                      </ResultPanel>
                      <ResultPanel title={`${snapshotMode === "server" ? "Server" : "Client"} snapshot`}>
                        {runtimeSnapshot ? (
                          <div className="space-y-3">
                            <p className="break-all font-mono text-[10px] text-violet-300">
                              v{runtimeSnapshot.snapshotVersion} · {runtimeSnapshot.etag}
                            </p>
                            {runtimeSnapshot.values.map((value) => (
                              <EffectiveValue key={value.entryId} value={value} />
                            ))}
                            {runtimeSnapshot.values.length === 0 && <p>No visible values.</p>}
                          </div>
                        ) : (
                          <p>Fetch the client-safe or privileged server snapshot.</p>
                        )}
                      </ResultPanel>
                    </div>
                    <div className="flex flex-col gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-4 sm:flex-row sm:items-end">
                      <TextField
                        label="Known snapshot version"
                        name="knownConfigSnapshotVersion"
                        onChange={setKnownSnapshotVersion}
                        value={knownSnapshotVersion}
                      />
                      <Button
                        data-ui-action="check-config-updates"
                        onClick={() => void checkUpdates()}
                        type="button"
                        variant="outline"
                      >
                        <RefreshCw className="size-4" /> Check updates
                      </Button>
                      {updateResult && (
                        <p className="pb-2 text-xs text-slate-400">
                          {updateResult.changed ? "Update available" : "Current"} · latest {" "}
                          {updateResult.currentSnapshotVersion}
                        </p>
                      )}
                    </div>
                  </CardContent>
                </Card>
              </>
            )}

            <Card data-ui-action="list-config-snapshots">
              <CardHeader>
                <CardTitle>Environment snapshot history</CardTitle>
                <CardDescription>
                  Every publish, rollback, archive, or restore creates one immutable snapshot.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                  {(snapshots.data?.snapshots ?? []).map((snapshot) => (
                    <div
                      className="rounded-xl border border-white/8 bg-white/[0.025] p-4"
                      key={snapshot.id}
                    >
                      <div className="flex items-center gap-2">
                        <History className="size-4 text-violet-300" />
                        <p className="text-sm font-semibold text-slate-200">
                          Snapshot {snapshot.version}
                        </p>
                      </div>
                      <p className="mt-2 text-xs text-slate-500">
                        {snapshot.entryCount} published entries
                      </p>
                      <p className="mt-1 text-[10px] text-slate-600">
                        {new Date(snapshot.createdAt).toLocaleString()}
                      </p>
                    </div>
                  ))}
                  {!snapshots.isLoading && (snapshots.data?.snapshots.length ?? 0) === 0 && (
                    <EmptyState text="No environment snapshots have been published." />
                  )}
                </div>
              </CardContent>
            </Card>
          </>
        )}
      </fieldset>
    </div>
  );
}

function ScopeSelector({
  applicationId,
  applications,
  environmentId,
  environments,
  onApplication,
  onEnvironment,
  onTenant,
  tenantId,
  tenants,
}: {
  applicationId: string;
  applications: Array<{ displayName: string; id: string; slug: string }>;
  environmentId: string;
  environments: Array<{ displayName: string; id: string; slug: string }>;
  onApplication: (value: string) => void;
  onEnvironment: (value: string) => void;
  onTenant: (value: string) => void;
  tenantId: string;
  tenants: Array<{ displayName: string; id: string; slug: string }>;
}) {
  return (
    <Card>
      <CardContent className="grid gap-4 pt-5 sm:pt-6 lg:grid-cols-3">
        <ScopeSelect
          ariaLabel="Configuration tenant"
          disabled={false}
          onChange={onTenant}
          options={tenants}
          placeholder="Select tenant"
          value={tenantId}
        />
        <ScopeSelect
          ariaLabel="Configuration application"
          disabled={!tenantId}
          onChange={onApplication}
          options={applications}
          placeholder="Select application"
          value={applicationId}
        />
        <ScopeSelect
          ariaLabel="Configuration environment"
          disabled={!applicationId}
          onChange={onEnvironment}
          options={environments}
          placeholder="Select environment"
          value={environmentId}
        />
      </CardContent>
    </Card>
  );
}

function ScopeSelect({
  ariaLabel,
  disabled,
  onChange,
  options,
  placeholder,
  value,
}: {
  ariaLabel: string;
  disabled: boolean;
  onChange: (value: string) => void;
  options: Array<{ displayName: string; id: string; slug: string }>;
  placeholder: string;
  value: string;
}) {
  return (
    <select
      aria-label={ariaLabel}
      className={inputClassName}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
      value={value}
    >
      <option value="">{placeholder}</option>
      {options.map((option) => (
        <option key={option.id} value={option.id}>
          {option.displayName} ({option.slug})
        </option>
      ))}
    </select>
  );
}

function TextField({
  label,
  name,
  onChange,
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  value: string;
}) {
  return (
    <label className="min-w-0 flex-1">
      <span className={labelClassName}>{label}</span>
      <input
        aria-label={label}
        className={inputClassName}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        value={value}
      />
    </label>
  );
}

function TextAreaField({
  label,
  name,
  onChange,
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  value: string;
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <textarea
        aria-label={label}
        className={`${inputClassName} min-h-20 py-2`}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        value={value}
      />
    </label>
  );
}

function SelectField({
  label,
  name,
  onChange,
  options,
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  options: Array<{ label: string; value: string }>;
  value: string;
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <select
        aria-label={label}
        className={inputClassName}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        value={value}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function ResultPanel({ children, title }: { children: React.ReactNode; title: string }) {
  return (
    <div className="rounded-xl border border-white/8 bg-slate-950/60 p-4 text-xs text-slate-500">
      <p className="mb-3 font-semibold uppercase tracking-[0.12em] text-slate-400">{title}</p>
      {children}
    </div>
  );
}

function EffectiveValue({ value }: { value: ConfigEffectiveValueRecord }) {
  return (
    <div className="rounded-lg border border-white/8 bg-white/[0.025] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <code className="text-violet-200">{value.key}</code>
        <span className="text-[10px] uppercase tracking-wider text-slate-600">
          {value.reason.replace("CONFIG_EVALUATION_REASON_", "")}
        </span>
      </div>
      <pre className="mt-2 overflow-auto whitespace-pre-wrap text-xs text-slate-300">
        {formatValue(value.value)}
      </pre>
    </div>
  );
}

function JsonBlock({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="mb-1 text-[10px] uppercase tracking-wider text-slate-600">{label}</p>
      <pre className="max-h-64 overflow-auto whitespace-pre-wrap text-[10px] leading-5 text-slate-400">
        {value}
      </pre>
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return (
    <div className="col-span-full rounded-xl border border-dashed border-white/10 px-4 py-7 text-center text-xs text-slate-500">
      <CircleAlert className="mx-auto mb-2 size-4 text-slate-600" />
      {text}
    </div>
  );
}

function formatValue(value: ConfigValueInput): string {
  if ("booleanValue" in value) return String(value.booleanValue);
  if ("integerValue" in value) return String(value.integerValue);
  if ("doubleValue" in value) return String(value.doubleValue);
  if ("stringValue" in value) return value.stringValue;
  try {
    return JSON.stringify(JSON.parse(value.jsonValue), null, 2);
  } catch {
    return value.jsonValue;
  }
}

function kindLabel(kind: ConfigValueKind): string {
  return kind.replace("CONFIG_VALUE_KIND_", "").toLowerCase();
}

function visibilityLabel(visibility: ConfigVisibility): string {
  return visibility === ConfigVisibilityObject.CONFIG_VISIBILITY_SERVER
    ? "server only"
    : "client visible";
}

const valueKindOptions = [
  { label: "Boolean", value: ConfigValueKindObject.CONFIG_VALUE_KIND_BOOLEAN },
  { label: "Integer", value: ConfigValueKindObject.CONFIG_VALUE_KIND_INTEGER },
  { label: "Double", value: ConfigValueKindObject.CONFIG_VALUE_KIND_DOUBLE },
  { label: "String", value: ConfigValueKindObject.CONFIG_VALUE_KIND_STRING },
  { label: "JSON object", value: ConfigValueKindObject.CONFIG_VALUE_KIND_JSON },
];

const visibilityOptions = [
  { label: "Client visible", value: ConfigVisibilityObject.CONFIG_VISIBILITY_CLIENT },
  { label: "Server only", value: ConfigVisibilityObject.CONFIG_VISIBILITY_SERVER },
];
