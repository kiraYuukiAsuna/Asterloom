"use client";

import {
  Archive,
  Braces,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  FlaskConical,
  Gauge,
  History,
  LoaderCircle,
  Plus,
  Radio,
  RotateCcw,
  Save,
  Search,
  Send,
} from "lucide-react";
import { type FormEvent, useMemo, useState } from "react";
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
  FeatureResourceStatusObject,
  FeatureValidationSeverityObject,
  FeatureValueKindObject,
} from "@/lib/api/generated/models";
import {
  archiveFlag,
  createFlag,
  evaluateFlag,
  featureErrorMessage,
  getFlag,
  isFeatureVersionConflict,
  listFlagRevisions,
  listFlags,
  publishFlag,
  restoreFlag,
  rollbackFlag,
  simulateFlag,
  updateFlagDraft,
  validateFlagDraft,
  type FeatureContextInput,
  type FeatureEvaluationResult,
  type FeatureFlagRecord,
  type FeatureScope,
  type FeatureValidationResult,
  type FeatureValueInput,
  type FeatureValueKind,
} from "@/lib/api/feature-management";
import {
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import { listSegments } from "@/lib/api/targeting-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import {
  createFeatureDefinitionDraft,
  FeatureDefinitionEditor,
  toFeatureDefinitionInput,
  type FeatureDefinitionDraft,
} from "./definition-editor";
import { useFeatureSelection } from "./feature-store";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const activeStatus = FeatureResourceStatusObject.FEATURE_RESOURCE_STATUS_ACTIVE;

type AttributeDraft = {
  key: string;
  kind: "text" | "truth" | "numeric";
  rawValue: string;
};

export function FeatureWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const tenantId = useFeatureSelection((state) => state.tenantId);
  const applicationId = useFeatureSelection((state) => state.applicationId);
  const environmentId = useFeatureSelection((state) => state.environmentId);
  const flagId = useFeatureSelection((state) => state.flagId);
  const selectTenant = useFeatureSelection((state) => state.selectTenant);
  const selectApplication = useFeatureSelection((state) => state.selectApplication);
  const selectEnvironment = useFeatureSelection((state) => state.selectEnvironment);
  const selectFlag = useFeatureSelection((state) => state.selectFlag);

  const [pending, setPending] = useState("");
  const [query, setQuery] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [pageTokens, setPageTokens] = useState([""]);
  const [pageIndex, setPageIndex] = useState(0);
  const [selectedFlag, setSelectedFlag] = useState<FeatureFlagRecord>();

  const [createKey, setCreateKey] = useState("");
  const [createName, setCreateName] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [createKind, setCreateKind] = useState<FeatureValueKind>(
    FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
  );
  const [createDefinition, setCreateDefinition] = useState<FeatureDefinitionDraft>(
    () =>
      createFeatureDefinitionDraft(
        FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
      ),
  );
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editDefinition, setEditDefinition] = useState<FeatureDefinitionDraft>();
  const [validation, setValidation] = useState<FeatureValidationResult>();

  const [targetingKey, setTargetingKey] = useState("");
  const [userId, setUserId] = useState("");
  const [clientVersion, setClientVersion] = useState("");
  const [platform, setPlatform] = useState("");
  const [region, setRegion] = useState("");
  const [language, setLanguage] = useState("");
  const [attributes, setAttributes] = useState<AttributeDraft[]>([]);
  const [useDraft, setUseDraft] = useState(false);
  const [evaluation, setEvaluation] = useState<FeatureEvaluationResult>();
  const [evaluationSource, setEvaluationSource] = useState<"runtime" | "simulation">();

  const tenants = useSWR("feature-scope-tenants", () =>
    listTenants({ includeArchived: false, pageSize: 100, pageToken: "", query: "" }),
  );
  const applications = useSWR(
    tenantId ? ["feature-scope-applications", tenantId] : null,
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
      ? ["feature-scope-environments", tenantId, applicationId]
      : null,
    () =>
      listEnvironments(tenantId, applicationId, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const scope = useMemo<FeatureScope | undefined>(
    () =>
      tenantId && applicationId && environmentId
        ? { tenantId, applicationId, environmentId }
        : undefined,
    [applicationId, environmentId, tenantId],
  );
  const flags = useSWR(
    scope
      ? [
          "feature-flags",
          scope.tenantId,
          scope.applicationId,
          scope.environmentId,
          query,
          includeArchived,
          pageTokens[pageIndex],
        ]
      : null,
    () =>
      listFlags(scope!, {
        includeArchived,
        pageSize: 25,
        pageToken: pageTokens[pageIndex],
        query,
      }),
    { keepPreviousData: true },
  );
  const segments = useSWR(
    scope
      ? [
          "feature-active-segments",
          scope.tenantId,
          scope.applicationId,
          scope.environmentId,
        ]
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
    selectedFlag
      ? ["feature-revisions", selectedFlag.id, selectedFlag.publishedRevision]
      : null,
    () => listFlagRevisions(selectedFlag!, 100),
  );

  const activeSegments = (segments.data?.segments ?? []).filter(
    (segment) => segment.status.endsWith("_ACTIVE"),
  );

  function resetCollectionState() {
    setPageTokens([""]);
    setPageIndex(0);
    setSelectedFlag(undefined);
    setEditDefinition(undefined);
    setValidation(undefined);
    setEvaluation(undefined);
    selectFlag("");
  }

  function changeTenant(value: string) {
    selectTenant(value);
    resetCollectionState();
  }

  function changeApplication(value: string) {
    selectApplication(value);
    resetCollectionState();
  }

  function changeEnvironment(value: string) {
    selectEnvironment(value);
    resetCollectionState();
  }

  function changeListFilter(value: string, archived: boolean = includeArchived) {
    setQuery(value);
    setIncludeArchived(archived);
    resetCollectionState();
  }

  async function runMutation<T>(
    key: string,
    work: () => Promise<T>,
    successMessage: string,
  ): Promise<T | undefined> {
    setPending(key);
    try {
      const result = await work();
      await flags.mutate();
      toast.success(translate(successMessage));
      return result;
    } catch (error) {
      await flags.mutate();
      toast.error(
        translate(isFeatureVersionConflict(error)
          ? "This flag changed in another session. Latest data loaded; review it and retry."
          : featureErrorMessage(error)),
      );
      return undefined;
    } finally {
      setPending("");
    }
  }

  function loadFlagIntoEditor(flag: FeatureFlagRecord) {
    setSelectedFlag(flag);
    selectFlag(flag.id);
    setEditName(flag.displayName);
    setEditDescription(flag.description);
    setEditDefinition(
      createFeatureDefinitionDraft(flag.valueKind, flag.draftDefinition),
    );
    setValidation(undefined);
    setEvaluation(undefined);
  }

  async function inspectFlag(record: FeatureFlagRecord) {
    if (!scope) return;
    setPending(`get-${record.id}`);
    try {
      loadFlagIntoEditor(await getFlag(scope, record.id));
    } catch (error) {
      toast.error(translate(featureErrorMessage(error)));
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
        createFlag(csrfToken, scope, {
          definition: toFeatureDefinitionInput(createDefinition, createKind),
          description: createDescription,
          displayName: createName,
          key: createKey,
          valueKind: createKind,
        }),
      "Feature flag draft created.",
    );
    if (created) {
      setCreateKey("");
      setCreateName("");
      setCreateDescription("");
      setCreateDefinition(createFeatureDefinitionDraft(createKind));
      loadFlagIntoEditor(created);
    }
  }

  async function submitDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedFlag || !editDefinition) return;
    const updated = await runMutation(
      `update-${selectedFlag.id}`,
      () =>
        updateFlagDraft(csrfToken, selectedFlag, {
          definition: toFeatureDefinitionInput(editDefinition, selectedFlag.valueKind),
          description: editDescription,
          displayName: editName,
        }),
      "Draft saved. Validate it before publishing.",
    );
    if (updated) loadFlagIntoEditor(updated);
  }

  async function validateDraft() {
    if (!selectedFlag) return;
    setPending(`validate-${selectedFlag.id}`);
    try {
      const result = await validateFlagDraft(csrfToken, selectedFlag);
      setValidation(result);
      toast[result.valid ? "success" : "error"](
        result.valid ? "Draft passed validation." : "Draft has blocking validation issues.",
      );
    } catch (error) {
      toast.error(translate(featureErrorMessage(error)));
    } finally {
      setPending("");
    }
  }

  async function publishCurrent() {
    if (!selectedFlag || !window.confirm(translate("Publish this draft as a new immutable revision?"))) {
      return;
    }
    const published = await runMutation(
      `publish-${selectedFlag.id}`,
      () => publishFlag(csrfToken, selectedFlag),
      "Feature flag published.",
    );
    if (published) {
      loadFlagIntoEditor(published);
      await revisions.mutate();
    }
  }

  async function rollbackCurrent(revision: number) {
    if (
      !selectedFlag ||
      !window.confirm(translate(`Roll back by publishing revision ${revision} as a new revision?`))
    ) {
      return;
    }
    const rolledBack = await runMutation(
      `rollback-${revision}`,
      () => rollbackFlag(csrfToken, selectedFlag, revision),
      `Revision ${revision} republished as a new revision.`,
    );
    if (rolledBack) {
      loadFlagIntoEditor(rolledBack);
      await revisions.mutate();
    }
  }

  async function changeStatus(record: FeatureFlagRecord, restore: boolean) {
    if (!restore && !window.confirm(translate(`Archive ${record.displayName}?`))) return;
    const updated = await runMutation(
      `${restore ? "restore" : "archive"}-${record.id}`,
      () => (restore ? restoreFlag(csrfToken, record) : archiveFlag(csrfToken, record)),
      restore ? "Feature flag restored." : "Feature flag archived.",
    );
    if (updated && selectedFlag?.id === updated.id) loadFlagIntoEditor(updated);
  }

  function buildContext(): FeatureContextInput {
    return {
      attributes: attributes.map((attribute) => ({
        key: attribute.key,
        value: parseAttributeValue(attribute),
      })),
      clientVersion,
      language,
      platform,
      region,
      targetingKey,
      userId,
    };
  }

  async function runEvaluation(runtime: boolean) {
    if (!selectedFlag) return;
    const key = runtime ? "evaluate" : "simulate";
    setPending(key);
    try {
      const result = runtime
        ? await evaluateFlag(csrfToken, selectedFlag, buildContext())
        : await simulateFlag(csrfToken, selectedFlag, buildContext(), useDraft);
      setEvaluation(result);
      setEvaluationSource(runtime ? "runtime" : "simulation");
      toast.success(
        translate(runtime
          ? "Published flag evaluated through the runtime endpoint."
          : `${useDraft ? "Draft" : "Published"} simulation completed.`),
      );
    } catch (error) {
      toast.error(translate(featureErrorMessage(error)));
    } finally {
      setPending("");
    }
  }

  const selectedTenant = tenants.data?.tenants.find((item) => item.id === tenantId);
  const selectedApplication = applications.data?.applications.find(
    (item) => item.id === applicationId,
  );
  const selectedEnvironment = environments.data?.environments.find(
    (item) => item.id === environmentId,
  );

  return (
    <div
      className="space-y-6"
      data-feature-workspace
      data-hydrated={hydrated ? "true" : "false"}
    >
      <fieldset className="contents" disabled={!hydrated}>
        <section className="theme-hero-cyan overflow-hidden rounded-3xl border border-cyan-300/10 bg-[radial-gradient(circle_at_top_right,rgba(34,211,238,0.13),transparent_38%),linear-gradient(135deg,rgba(14,116,144,0.10),rgba(15,23,42,0.88)_55%,rgba(2,6,23,0.97))] p-6 sm:p-8">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <Badge variant="info">
                <Radio aria-hidden="true" className="size-3" /> {translate("OpenFeature provider live")}</Badge>
              <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
                {translate("Feature delivery control")}</h1>
              <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
                {translate("Author typed drafts, validate dependencies, publish immutable revisions, and exercise the exact runtime evaluator. Targeting and percentage rollout share deterministic bucketing v1 across the control plane and C# SDK.")}</p>
            </div>
            <div className="text-right text-xs leading-6 text-slate-500">
              <p>{selectedTenant?.displayName ?? "Choose a tenant"}</p>
              <p>{selectedApplication?.displayName ?? "Choose an application"}</p>
              <p className="text-cyan-300">
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
          onApplication={changeApplication}
          onEnvironment={changeEnvironment}
          onTenant={changeTenant}
          tenantId={tenantId}
          tenants={tenants.data?.tenants ?? []}
        />

        {!scope ? (
          <Card>
            <CardContent className="flex min-h-40 items-center justify-center pt-6 text-center text-sm text-slate-500">
              {translate("Select an active tenant, application, and environment to manage its flags.")}</CardContent>
          </Card>
        ) : (
          <>
            <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
              <FlagList
                data={flags.data?.flags ?? []}
                error={flags.error}
                includeArchived={includeArchived}
                isLoading={flags.isLoading}
                nextPageToken={flags.data?.nextPageToken ?? ""}
                onChangeStatus={changeStatus}
                onIncludeArchived={(value) => changeListFilter(query, value)}
                onInspect={inspectFlag}
                onNext={(token) => {
                  setPageTokens((current) => [
                    ...current.slice(0, pageIndex + 1),
                    token,
                  ]);
                  setPageIndex((current) => current + 1);
                }}
                onPrevious={() => setPageIndex((current) => Math.max(0, current - 1))}
                onQuery={(value) => changeListFilter(value)}
                pageIndex={pageIndex}
                pending={pending}
                query={query}
                selectedId={flagId}
              />
              <CreateFlagPanel
                definition={createDefinition}
                description={createDescription}
                displayName={createName}
                isPending={pending === "create"}
                keyValue={createKey}
                onDefinition={setCreateDefinition}
                onDescription={setCreateDescription}
                onDisplayName={setCreateName}
                onKey={setCreateKey}
                onKind={(kind) => {
                  setCreateKind(kind);
                  setCreateDefinition(createFeatureDefinitionDraft(kind));
                }}
                onSubmit={submitCreate}
                segments={activeSegments}
                valueKind={createKind}
              />
            </div>

            {selectedFlag && editDefinition && (
              <>
                <DraftPanel
                  definition={editDefinition}
                  description={editDescription}
                  displayName={editName}
                  flag={selectedFlag}
                  onDefinition={setEditDefinition}
                  onDescription={setEditDescription}
                  onDisplayName={setEditName}
                  onPublish={publishCurrent}
                  onSubmit={submitDraft}
                  onValidate={validateDraft}
                  pending={pending}
                  segments={activeSegments}
                  validation={validation}
                />
                <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,0.85fr)_minmax(0,1.15fr)]">
                  <RevisionPanel
                    error={revisions.error}
                    flag={selectedFlag}
                    isLoading={revisions.isLoading}
                    onRollback={rollbackCurrent}
                    pending={pending}
                    revisions={revisions.data?.revisions ?? []}
                  />
                  <EvaluationPanel
                    attributes={attributes}
                    clientVersion={clientVersion}
                    evaluation={evaluation}
                    evaluationSource={evaluationSource}
                    flag={selectedFlag}
                    language={language}
                    onAddAttribute={() =>
                      setAttributes((current) => [
                        ...current,
                        { key: "", kind: "text", rawValue: "" },
                      ])
                    }
                    onAttribute={setAttributes}
                    onClientVersion={setClientVersion}
                    onEvaluate={() => runEvaluation(true)}
                    onLanguage={setLanguage}
                    onPlatform={setPlatform}
                    onRegion={setRegion}
                    onSimulate={() => runEvaluation(false)}
                    onTargetingKey={setTargetingKey}
                    onUseDraft={setUseDraft}
                    onUserId={setUserId}
                    pending={pending}
                    platform={platform}
                    region={region}
                    targetingKey={targetingKey}
                    useDraft={useDraft}
                    userId={userId}
                  />
                </div>
              </>
            )}
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
      <CardHeader>
        <CardTitle>{translate("Environment scope")}</CardTitle>
        <CardDescription>
          {translate("Flag keys, revisions, segments, and rollouts are isolated to one environment.")}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-3">
        <ScopeSelect
          label={translate("Tenant")}
          name="Feature tenant"
          onChange={onTenant}
          options={tenants}
          placeholder={translate("Select tenant")}
          value={tenantId}
        />
        <ScopeSelect
          disabled={!tenantId}
          label={translate("Application")}
          name="Feature application"
          onChange={onApplication}
          options={applications}
          placeholder={translate("Select application")}
          value={applicationId}
        />
        <ScopeSelect
          disabled={!applicationId}
          label={translate("Environment")}
          name="Feature environment"
          onChange={onEnvironment}
          options={environments}
          placeholder={translate("Select environment")}
          value={environmentId}
        />
      </CardContent>
    </Card>
  );
}

function ScopeSelect({
  disabled = false,
  label,
  name,
  onChange,
  options,
  placeholder,
  value,
}: {
  disabled?: boolean;
  label: string;
  name: string;
  onChange: (value: string) => void;
  options: Array<{ displayName: string; id: string; slug: string }>;
  placeholder: string;
  value: string;
}) {
  return (
    <SearchableSelect
      ariaLabel={name}
      className={inputClassName}
      disabled={disabled}
      emptyLabel={placeholder}
      label={label}
      labelClassName={labelClassName}
      onChange={onChange}
      options={options.map((option) => ({
        label: `${option.displayName} (${option.slug})`,
        value: option.id,
      }))}
      value={value}
    />
  );
}

function FlagList({
  data,
  error,
  includeArchived,
  isLoading,
  nextPageToken,
  onChangeStatus,
  onIncludeArchived,
  onInspect,
  onNext,
  onPrevious,
  onQuery,
  pageIndex,
  pending,
  query,
  selectedId,
}: {
  data: FeatureFlagRecord[];
  error: unknown;
  includeArchived: boolean;
  isLoading: boolean;
  nextPageToken: string;
  onChangeStatus: (flag: FeatureFlagRecord, restore: boolean) => void;
  onIncludeArchived: (value: boolean) => void;
  onInspect: (flag: FeatureFlagRecord) => void;
  onNext: (token: string) => void;
  onPrevious: () => void;
  onQuery: (value: string) => void;
  pageIndex: number;
  pending: string;
  query: string;
  selectedId: string;
}) {
  return (
    <Card data-ui-action="list-flags">
      <CardHeader>
        <CardTitle>{translate("Flags")}</CardTitle>
        <CardDescription>{translate("Search, inspect, archive, and restore environment flags.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto]">
          <label className="relative">
            <Search className="absolute left-3 top-3 size-4 text-slate-600" />
            <input
              aria-label={translate("Search feature flags")}
              className={`${inputClassName} pl-10`}
              onChange={(event) => onQuery(event.target.value)}
              placeholder={translate("Search key or display name")}
              value={query}
            />
          </label>
          <label className="flex h-10 items-center gap-2 text-xs text-slate-400">
            <input
              aria-label={translate("Include archived flags")}
              checked={includeArchived}
              onChange={(event) => onIncludeArchived(event.target.checked)}
              type="checkbox"
            />
            {translate("Include archived")}</label>
        </div>
        <div className="mt-4 space-y-2">
          {isLoading ? (
            <LoadingLabel />
          ) : error ? (
            <InlineError message={featureErrorMessage(error)} />
          ) : data.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-600">{translate("No flags found.")}</p>
          ) : (
            data.map((flag) => {
              const active = flag.status === activeStatus;
              return (
                <div
                  className={cn(
                    "rounded-xl border p-4 transition",
                    selectedId === flag.id
                      ? "border-cyan-400/30 bg-cyan-400/[0.06]"
                      : "border-white/8 bg-white/[0.025]",
                  )}
                  data-testid={`feature-flag-${flag.key}`}
                  key={flag.id}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <p className="font-medium text-white">{flag.displayName}</p>
                        <Badge variant={active ? "success" : "planned"}>
                          {translate(active ? "Active" : "Archived")}
                        </Badge>
                      </div>
                      <p className="mt-1 font-mono text-xs text-cyan-300">{flag.key}</p>
                      <p className="mt-2 text-xs text-slate-500">
                        {prettyKind(flag.valueKind)} {" "}{translate("· draft")}{" "}{flag.draftRevision} {" "}{translate("· published")}{" "}
                        {flag.publishedRevision || "—"} {" "}{translate("· version")}{" "}{flag.version}
                      </p>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        data-ui-action="get-flag"
                        disabled={pending === `get-${flag.id}`}
                        onClick={() => onInspect(flag)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {pending === `get-${flag.id}` ? (
                          <LoaderCircle className="size-3.5 animate-spin" />
                        ) : (
                          <Braces className="size-3.5" />
                        )}
                        {translate("Inspect")}</Button>
                      <Button
                        data-ui-action={active ? "archive-flag" : "restore-flag"}
                        disabled={pending.endsWith(flag.id)}
                        onClick={() => onChangeStatus(flag, !active)}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        {active ? (
                          <Archive className="size-3.5" />
                        ) : (
                          <RotateCcw className="size-3.5" />
                        )}
                        {translate(active ? "Archive" : "Restore")}
                      </Button>
                    </div>
                  </div>
                </div>
              );
            })
          )}
        </div>
        <div className="mt-4 flex items-center justify-between">
          <Button disabled={pageIndex === 0} onClick={onPrevious} size="sm" type="button" variant="ghost">
            <ChevronLeft className="size-3.5" /> {translate("Previous")}</Button>
          <span className="text-xs text-slate-600">{translate("Page")}{" "}{pageIndex + 1}</span>
          <Button disabled={!nextPageToken} onClick={() => onNext(nextPageToken)} size="sm" type="button" variant="ghost">
            {translate("Next")}<ChevronRight className="size-3.5" />
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function CreateFlagPanel({
  definition,
  description,
  displayName,
  isPending,
  keyValue,
  onDefinition,
  onDescription,
  onDisplayName,
  onKey,
  onKind,
  onSubmit,
  segments,
  valueKind,
}: {
  definition: FeatureDefinitionDraft;
  description: string;
  displayName: string;
  isPending: boolean;
  keyValue: string;
  onDefinition: (value: FeatureDefinitionDraft) => void;
  onDescription: (value: string) => void;
  onDisplayName: (value: string) => void;
  onKey: (value: string) => void;
  onKind: (value: FeatureValueKind) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  segments: Array<{ displayName: string; id: string; key: string }>;
  valueKind: FeatureValueKind;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Create flag draft")}</CardTitle>
        <CardDescription>
          {translate("A value type and key are immutable. The initial definition remains a draft.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-5" data-ui-action="create-flag" onSubmit={onSubmit}>
          <div className="grid gap-3 sm:grid-cols-2">
            <TextField label={translate("Flag key")} name="createFlagKey" onChange={onKey} required value={keyValue} />
            <label>
              <span className={labelClassName}>{translate("Value type")}</span>
              <select
                className={inputClassName}
                name="createFlagValueKind"
                onChange={(event) => onKind(event.target.value as FeatureValueKind)}
                value={valueKind}
              >
                {valueKindOptions.map((option) => (
                  <option key={option.value} value={option.value}>{translate(option.label)}</option>
                ))}
              </select>
            </label>
            <TextField label={translate("Display name")} name="createFlagDisplayName" onChange={onDisplayName} required value={displayName} />
            <label>
              <span className={labelClassName}>{translate("Description")}</span>
              <textarea
                className={`${inputClassName} min-h-20 py-2`}
                name="createFlagDescription"
                onChange={(event) => onDescription(event.target.value)}
                value={description}
              />
            </label>
          </div>
          <FeatureDefinitionEditor
            draft={definition}
            idPrefix="create"
            onChange={onDefinition}
            segments={segments}
            valueKind={valueKind}
          />
          <Button disabled={isPending} type="submit">
            {isPending ? <LoaderCircle className="size-4 animate-spin" /> : <Plus className="size-4" />}
            {translate("Create flag")}</Button>
        </form>
      </CardContent>
    </Card>
  );
}

function DraftPanel({
  definition,
  description,
  displayName,
  flag,
  onDefinition,
  onDescription,
  onDisplayName,
  onPublish,
  onSubmit,
  onValidate,
  pending,
  segments,
  validation,
}: {
  definition: FeatureDefinitionDraft;
  description: string;
  displayName: string;
  flag: FeatureFlagRecord;
  onDefinition: (value: FeatureDefinitionDraft) => void;
  onDescription: (value: string) => void;
  onDisplayName: (value: string) => void;
  onPublish: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onValidate: () => void;
  pending: string;
  segments: Array<{ displayName: string; id: string; key: string }>;
  validation?: FeatureValidationResult;
}) {
  const active = flag.status === activeStatus;
  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-white/8 bg-cyan-400/[0.025]">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <CardTitle>{translate("Draft ·")}{" "}{flag.key}</CardTitle>
            <CardDescription>
              {prettyKind(flag.valueKind)} {" "}{translate("· draft revision")}{" "}{flag.draftRevision} {" "}{translate("· resource version")}{" "}{flag.version}
            </CardDescription>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              data-ui-action="validate-flag-draft"
              disabled={!active || pending.length > 0}
              onClick={onValidate}
              size="sm"
              type="button"
              variant="outline"
            >
              {pending === `validate-${flag.id}` ? <LoaderCircle className="size-3.5 animate-spin" /> : <CheckCircle2 className="size-3.5" />}
              {translate("Validate")}</Button>
            <Button
              data-ui-action="publish-flag"
              disabled={!active || pending.length > 0}
              onClick={onPublish}
              size="sm"
              type="button"
            >
              {pending === `publish-${flag.id}` ? <LoaderCircle className="size-3.5 animate-spin" /> : <Send className="size-3.5" />}
              {translate("Publish")}</Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="pt-6">
        {!active && <InlineError message={translate("Restore this flag before changing or publishing its draft.")} />}
        <form className="mt-4 space-y-5" data-ui-action="update-flag-draft" onSubmit={onSubmit}>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={translate("Display name")} name="editFlagDisplayName" onChange={onDisplayName} required value={displayName} />
            <label>
              <span className={labelClassName}>{translate("Description")}</span>
              <textarea
                className={`${inputClassName} min-h-20 py-2`}
                name="editFlagDescription"
                onChange={(event) => onDescription(event.target.value)}
                value={description}
              />
            </label>
          </div>
          <FeatureDefinitionEditor
            draft={definition}
            idPrefix="edit"
            onChange={onDefinition}
            segments={segments}
            valueKind={flag.valueKind}
          />
          <Button disabled={!active || pending.length > 0} type="submit">
            {pending === `update-${flag.id}` ? <LoaderCircle className="size-4 animate-spin" /> : <Save className="size-4" />}
            {translate("Save draft")}</Button>
        </form>
        {validation && <ValidationResult result={validation} />}
      </CardContent>
    </Card>
  );
}

function ValidationResult({ result }: { result: FeatureValidationResult }) {
  return (
    <div className={cn("mt-6 rounded-2xl border p-5", result.valid ? "border-emerald-400/20 bg-emerald-400/[0.06]" : "border-rose-400/20 bg-rose-400/[0.06]")}>
      <div className="flex items-center gap-3">
        {result.valid ? <CheckCircle2 className="size-5 text-emerald-300" /> : <CircleAlert className="size-5 text-rose-300" />}
        <div>
          <p className="font-semibold text-white">{translate(result.valid ? "Draft is publishable" : "Draft needs attention")}</p>
          <p className="mt-1 font-mono text-[10px] text-slate-600">{translate("SHA-256")}{" "}{result.definitionHash}</p>
        </div>
      </div>
      {result.issues.length > 0 && (
        <div className="mt-4 space-y-2">
          {result.issues.map((issue, index) => (
            <div className="rounded-lg border border-white/8 bg-black/10 p-3 text-xs" key={`${issue.code}-${issue.path}-${index}`}>
              <p className={issue.severity === FeatureValidationSeverityObject.FEATURE_VALIDATION_SEVERITY_ERROR ? "text-rose-200" : "text-amber-200"}>
                {issue.code} · {issue.path || "definition"}
              </p>
              <p className="mt-1 text-slate-400">{issue.message}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function RevisionPanel({
  error,
  flag,
  isLoading,
  onRollback,
  pending,
  revisions,
}: {
  error: unknown;
  flag: FeatureFlagRecord;
  isLoading: boolean;
  onRollback: (revision: number) => void;
  pending: string;
  revisions: Array<{ publishedAt: string; revision: number; sourceRevision: number }>;
}) {
  return (
    <Card data-ui-action="list-flag-revisions">
      <CardHeader>
        <CardTitle className="flex items-center gap-2"><History className="size-4 text-cyan-300" /> {" "}{translate("Published revisions")}</CardTitle>
        <CardDescription>{translate("Every publish and rollback appends an immutable revision.")}</CardDescription>
      </CardHeader>
      <CardContent>
        {isLoading ? <LoadingLabel /> : error ? <InlineError message={featureErrorMessage(error)} /> : revisions.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-600">{translate("This flag has not been published.")}</p>
        ) : (
          <div className="space-y-2">
            {revisions.map((revision) => (
              <div className="flex items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.025] p-3" key={revision.revision}>
                <div>
                  <p className="text-sm font-medium text-slate-200">{translate("Revision")}{" "}{revision.revision}</p>
                  <p className="mt-1 text-xs text-slate-600">
                    {formatDateTime(revision.publishedAt)}
                    {translate(revision.sourceRevision > 0 ? ` · restored from ${revision.sourceRevision}` : "")}
                  </p>
                </div>
                <Button
                  data-ui-action="rollback-flag"
                  disabled={flag.status !== activeStatus || revision.revision === flag.publishedRevision || pending.length > 0}
                  onClick={() => onRollback(revision.revision)}
                  size="sm"
                  type="button"
                  variant="ghost"
                >
                  {pending === `rollback-${revision.revision}` ? <LoaderCircle className="size-3.5 animate-spin" /> : <RotateCcw className="size-3.5" />}
                  {translate("Roll back")}</Button>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function EvaluationPanel({
  attributes,
  clientVersion,
  evaluation,
  evaluationSource,
  flag,
  language,
  onAddAttribute,
  onAttribute,
  onClientVersion,
  onEvaluate,
  onLanguage,
  onPlatform,
  onRegion,
  onSimulate,
  onTargetingKey,
  onUseDraft,
  onUserId,
  pending,
  platform,
  region,
  targetingKey,
  useDraft,
  userId,
}: {
  attributes: AttributeDraft[];
  clientVersion: string;
  evaluation?: FeatureEvaluationResult;
  evaluationSource?: "runtime" | "simulation";
  flag: FeatureFlagRecord;
  language: string;
  onAddAttribute: () => void;
  onAttribute: (value: AttributeDraft[]) => void;
  onClientVersion: (value: string) => void;
  onEvaluate: () => void;
  onLanguage: (value: string) => void;
  onPlatform: (value: string) => void;
  onRegion: (value: string) => void;
  onSimulate: () => void;
  onTargetingKey: (value: string) => void;
  onUseDraft: (value: boolean) => void;
  onUserId: (value: string) => void;
  pending: string;
  platform: string;
  region: string;
  targetingKey: string;
  useDraft: boolean;
  userId: string;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2"><FlaskConical className="size-4 text-cyan-300" /> {" "}{translate("Evaluation lab")}</CardTitle>
        <CardDescription>
          {translate("Compare admin simulation with the published runtime endpoint used by OpenFeature.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <TextField label={translate("Targeting key")} name="featureTargetingKey" onChange={onTargetingKey} required value={targetingKey} />
          <TextField label={translate("User ID")} name="featureUserId" onChange={onUserId} value={userId} />
          <TextField label={translate("Client version")} name="featureClientVersion" onChange={onClientVersion} value={clientVersion} />
          <TextField label={translate("Platform")} name="featurePlatform" onChange={onPlatform} value={platform} />
          <TextField label={translate("Region")} name="featureRegion" onChange={onRegion} value={region} />
          <TextField label={translate("Language")} name="featureLanguage" onChange={onLanguage} value={language} />
        </div>
        <div className="mt-4 space-y-2">
          {attributes.map((attribute, index) => (
            <div className="grid gap-2 sm:grid-cols-[1fr_8rem_1fr_auto]" key={index}>
              <input aria-label={translate(`Custom attribute ${index + 1} key`)} className={inputClassName} onChange={(event) => onAttribute(replaceAt(attributes, index, { ...attribute, key: event.target.value }))} placeholder={translate("subscription.plan")} value={attribute.key} />
              <select aria-label={translate(`Custom attribute ${index + 1} type`)} className={inputClassName} onChange={(event) => onAttribute(replaceAt(attributes, index, { ...attribute, kind: event.target.value as AttributeDraft["kind"], rawValue: "" }))} value={attribute.kind}>
                <option value="text">{translate("Text")}</option><option value="truth">{translate("Boolean")}</option><option value="numeric">{translate("Number")}</option>
              </select>
              <input aria-label={translate(`Custom attribute ${index + 1} value`)} className={inputClassName} onChange={(event) => onAttribute(replaceAt(attributes, index, { ...attribute, rawValue: event.target.value }))} placeholder={translate("Value")} value={attribute.rawValue} />
              <Button aria-label={translate(`Remove custom attribute ${index + 1}`)} onClick={() => onAttribute(attributes.filter((_, candidate) => candidate !== index))} size="icon" type="button" variant="ghost"><Archive className="size-4" /></Button>
            </div>
          ))}
          <Button onClick={onAddAttribute} size="sm" type="button" variant="outline"><Plus className="size-3.5" /> {" "}{translate("Add context attribute")}</Button>
        </div>
        <div className="mt-5 flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-xs text-slate-400">
            <input checked={useDraft} onChange={(event) => onUseDraft(event.target.checked)} type="checkbox" />
            {translate("Simulate draft instead of published revision")}</label>
          <div className="ml-auto flex flex-wrap gap-2">
            <Button data-ui-action="simulate-flag" disabled={!targetingKey || pending.length > 0} onClick={onSimulate} type="button" variant="outline">
              {pending === "simulate" ? <LoaderCircle className="size-4 animate-spin" /> : <FlaskConical className="size-4" />} {translate("Simulate")}</Button>
            <Button data-ui-action="evaluate-flag" disabled={!targetingKey || !flag.publishedRevision || pending.length > 0} onClick={onEvaluate} type="button">
              {pending === "evaluate" ? <LoaderCircle className="size-4 animate-spin" /> : <Gauge className="size-4" />} {translate("Runtime evaluate")}</Button>
          </div>
        </div>
        {evaluation && <EvaluationResult result={evaluation} source={evaluationSource!} />}
      </CardContent>
    </Card>
  );
}

function EvaluationResult({ result, source }: { result: FeatureEvaluationResult; source: "runtime" | "simulation" }) {
  return (
    <div className="mt-6 rounded-2xl border border-cyan-400/20 bg-cyan-400/[0.06] p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-lg font-semibold text-white">{result.variantKey}</p>
          <p className="mt-1 text-xs text-slate-500">{translate(source === "runtime" ? "Runtime endpoint" : result.usedDraft ? "Draft simulation" : "Published simulation")} {" "}{translate("· revision")}{" "}{result.revision}</p>
        </div>
        <Badge variant="info">{prettyReason(result.reason)}</Badge>
      </div>
      <pre className="mt-4 overflow-x-auto rounded-xl border border-white/8 bg-black/20 p-4 text-xs text-cyan-100">{formatValue(result.value)}</pre>
      {result.bucketEvaluated && <p className="mt-3 text-xs text-slate-400">{translate("Bucket")}{" "}<span className="font-mono text-cyan-300">{result.bucket}</span> · {result.bucketingVersion}</p>}
      <ol className="mt-4 space-y-1 text-xs text-slate-500">
        {result.trace.map((trace, index) => <li key={index}>{index + 1}. {trace}</li>)}
      </ol>
    </div>
  );
}

function TextField({ label, name, onChange, required = false, value }: { label: string; name: string; onChange: (value: string) => void; required?: boolean; value: string }) {
  return <label><span className={labelClassName}>{label}</span><input className={inputClassName} name={name} onChange={(event) => onChange(event.target.value)} required={required} value={value} /></label>;
}

function InlineError({ message }: { message: string }) {
  return <div className="flex items-start gap-2 rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-3 text-sm text-rose-200"><CircleAlert className="mt-0.5 size-4 shrink-0" /><span>{translate(message)}</span></div>;
}

function LoadingLabel() {
  return <p className="flex items-center gap-2 py-4 text-sm text-slate-500"><LoaderCircle className="size-4 animate-spin" /> {" "}{translate("Loading…")}</p>;
}

function parseAttributeValue(attribute: AttributeDraft) {
  if (attribute.kind === "text") return { text: attribute.rawValue };
  if (attribute.kind === "truth") {
    const normalized = attribute.rawValue.trim().toLowerCase();
    if (normalized !== "true" && normalized !== "false") throw new Error(`Attribute ${attribute.key || "value"} requires true or false.`);
    return { truth: normalized === "true" };
  }
  const numeric = Number(attribute.rawValue);
  if (!Number.isFinite(numeric) || attribute.rawValue.trim().length === 0) throw new Error(`Attribute ${attribute.key || "value"} requires a finite number.`);
  return { numeric };
}

function formatValue(value: FeatureValueInput): string {
  if ("booleanValue" in value) return String(value.booleanValue);
  if ("stringValue" in value) return value.stringValue;
  if ("integerValue" in value) return String(value.integerValue);
  if ("doubleValue" in value) return String(value.doubleValue);
  return JSON.stringify(JSON.parse(value.objectJson), null, 2);
}

function prettyKind(value: string): string { return translate(value.replace("FEATURE_VALUE_KIND_", "").toLowerCase()); }
function prettyReason(value: string): string { return translate(value.replace("FEATURE_EVALUATION_REASON_", "").toLowerCase().replaceAll("_", " ")); }
function replaceAt<T>(values: T[], index: number, value: T): T[] { return values.map((candidate, candidateIndex) => candidateIndex === index ? value : candidate); }

const valueKindOptions: Array<{ label: string; value: FeatureValueKind }> = [
  { label: "Boolean", value: FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN },
  { label: "String", value: FeatureValueKindObject.FEATURE_VALUE_KIND_STRING },
  { label: "Integer", value: FeatureValueKindObject.FEATURE_VALUE_KIND_INTEGER },
  { label: "Double", value: FeatureValueKindObject.FEATURE_VALUE_KIND_DOUBLE },
  { label: "JSON object", value: FeatureValueKindObject.FEATURE_VALUE_KIND_OBJECT },
];
