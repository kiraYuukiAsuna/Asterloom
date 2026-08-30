"use client";

import {
  Archive,
  Braces,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  FlaskConical,
  Gauge,
  LoaderCircle,
  Plus,
  RotateCcw,
  Save,
  Search,
  SlidersHorizontal,
  Trash2,
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
import { TargetingResourceStatusObject } from "@/lib/api/generated/models";
import {
  archiveSegment,
  createSegment,
  getSegment,
  isTargetingVersionConflict,
  listSegments,
  listTargetingAttributes,
  restoreSegment,
  simulateTargeting,
  targetingErrorMessage,
  updateSegment,
  type TargetingBucketPreviewInput,
  type TargetingCatalog,
  type TargetingContextInput,
  type TargetingScope,
  type TargetingSegmentRecord,
  type TargetingSimulationResult,
  type TargetingValueInput,
} from "@/lib/api/targeting-management";
import {
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import {
  createRuleDraft,
  RuleEditor,
  toRuleInput,
  type TargetingRuleDraft,
} from "./rule-editor";
import { useTargetingSelection } from "./targeting-store";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const activeStatus =
  TargetingResourceStatusObject.TARGETING_RESOURCE_STATUS_ACTIVE;

type AttributeDraft = {
  key: string;
  kind: "text" | "truth" | "numeric";
  rawValue: string;
};

type AllocationDraft = {
  end: string;
  start: string;
  variant: string;
};

export function TargetingWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const tenantId = useTargetingSelection((state) => state.tenantId);
  const applicationId = useTargetingSelection((state) => state.applicationId);
  const environmentId = useTargetingSelection((state) => state.environmentId);
  const segmentId = useTargetingSelection((state) => state.segmentId);
  const selectTenant = useTargetingSelection((state) => state.selectTenant);
  const selectApplication = useTargetingSelection(
    (state) => state.selectApplication,
  );
  const selectEnvironment = useTargetingSelection(
    (state) => state.selectEnvironment,
  );
  const selectSegment = useTargetingSelection((state) => state.selectSegment);

  const [pending, setPending] = useState("");
  const [query, setQuery] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [pageTokens, setPageTokens] = useState([""]);
  const [pageIndex, setPageIndex] = useState(0);
  const [selectedSegment, setSelectedSegment] =
    useState<TargetingSegmentRecord>();

  const [createKey, setCreateKey] = useState("");
  const [createName, setCreateName] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [createRule, setCreateRule] = useState<TargetingRuleDraft>(() =>
    createRuleDraft(),
  );
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editRule, setEditRule] = useState<TargetingRuleDraft>(() =>
    createRuleDraft(),
  );

  const [targetingKey, setTargetingKey] = useState("");
  const [userId, setUserId] = useState("");
  const [clientVersion, setClientVersion] = useState("");
  const [platform, setPlatform] = useState("");
  const [region, setRegion] = useState("");
  const [language, setLanguage] = useState("");
  const [attributes, setAttributes] = useState<AttributeDraft[]>([]);
  const [bucketEnabled, setBucketEnabled] = useState(false);
  const [resourceType, setResourceType] = useState("feature");
  const [resourceKey, setResourceKey] = useState("");
  const [salt, setSalt] = useState("");
  const [allocations, setAllocations] = useState<AllocationDraft[]>([
    { variant: "enabled", start: "0", end: "100000" },
  ]);
  const [simulation, setSimulation] = useState<TargetingSimulationResult>();

  const catalog = useSWR("targeting-attribute-catalog", listTargetingAttributes);
  const tenants = useSWR("targeting-scope-tenants", () =>
    listTenants({
      includeArchived: false,
      pageSize: 100,
      pageToken: "",
      query: "",
    }),
  );
  const applications = useSWR(
    tenantId ? ["targeting-scope-applications", tenantId] : null,
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
      ? ["targeting-scope-environments", tenantId, applicationId]
      : null,
    () =>
      listEnvironments(tenantId, applicationId, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );

  const scope = useMemo<TargetingScope | undefined>(
    () =>
      tenantId && applicationId && environmentId
        ? { tenantId, applicationId, environmentId }
        : undefined,
    [applicationId, environmentId, tenantId],
  );
  const segments = useSWR(
    scope
      ? [
          "targeting-segments",
          scope.tenantId,
          scope.applicationId,
          scope.environmentId,
          query,
          includeArchived,
          pageTokens[pageIndex],
        ]
      : null,
    () =>
      listSegments(scope!, {
        includeArchived,
        pageSize: 25,
        pageToken: pageTokens[pageIndex],
        query,
      }),
    { keepPreviousData: true },
  );

  function resetCollectionState() {
    setPageTokens([""]);
    setPageIndex(0);
    setSelectedSegment(undefined);
    setSimulation(undefined);
    selectSegment("");
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

  function changeQuery(value: string) {
    setQuery(value);
    resetCollectionState();
  }

  function changeIncludeArchived(value: boolean) {
    setIncludeArchived(value);
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
      await segments.mutate();
      toast.success(successMessage);
      return result;
    } catch (error) {
      await segments.mutate();
      toast.error(
        isTargetingVersionConflict(error)
          ? "This segment changed in another session. Latest data loaded; review it and retry."
          : targetingErrorMessage(error),
      );
      return undefined;
    } finally {
      setPending("");
    }
  }

  async function replaceSegmentInCollection(updated: TargetingSegmentRecord) {
    await segments.mutate(
      (current) =>
        current
          ? {
              ...current,
              segments: current.segments.map((segment) =>
                segment.id === updated.id ? updated : segment,
              ),
            }
          : current,
      { revalidate: false },
    );
  }

  async function inspectSegment(record: TargetingSegmentRecord) {
    if (!scope) return;
    setPending(`get-${record.id}`);
    try {
      const detail = await getSegment(scope, record.id);
      setSelectedSegment(detail);
      selectSegment(detail.id);
      setEditName(detail.displayName);
      setEditDescription(detail.description);
      setEditRule(createRuleDraft(detail.rule));
      setSimulation(undefined);
    } catch (error) {
      toast.error(targetingErrorMessage(error));
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
        createSegment(csrfToken, scope, {
          key: createKey,
          displayName: createName,
          description: createDescription,
          rule: toRuleInput(createRule),
        }),
      "Targeting segment created.",
    );
    if (created) {
      setCreateKey("");
      setCreateName("");
      setCreateDescription("");
      setCreateRule(createRuleDraft());
      setSelectedSegment(created);
      selectSegment(created.id);
      setEditName(created.displayName);
      setEditDescription(created.description);
      setEditRule(createRuleDraft(created.rule));
      setSimulation(undefined);
    }
  }

  async function submitUpdate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedSegment) return;
    const updated = await runMutation(
      `update-${selectedSegment.id}`,
      () =>
        updateSegment(csrfToken, selectedSegment, {
          displayName: editName,
          description: editDescription,
          rule: toRuleInput(editRule),
        }),
      "Targeting segment updated.",
    );
    if (updated) {
      await replaceSegmentInCollection(updated);
      setSelectedSegment(updated);
      setEditName(updated.displayName);
      setEditDescription(updated.description);
      setEditRule(createRuleDraft(updated.rule));
    }
  }

  async function changeStatus(record: TargetingSegmentRecord, restore: boolean) {
    if (!restore && !window.confirm(`Archive ${record.displayName}?`)) {
      return;
    }
    const updated = await runMutation(
      `${restore ? "restore" : "archive"}-${record.id}`,
      () =>
        restore
          ? restoreSegment(csrfToken, record)
          : archiveSegment(csrfToken, record),
      restore ? "Targeting segment restored." : "Targeting segment archived.",
    );
    if (updated) {
      await replaceSegmentInCollection(updated);
      if (selectedSegment?.id === updated.id) {
        setSelectedSegment(updated);
      }
    }
  }

  async function submitSimulation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!scope || !segmentId) return;
    setPending("simulate");
    try {
      const context: TargetingContextInput = {
        targetingKey,
        userId,
        clientVersion,
        platform,
        region,
        language,
        attributes: attributes.map((attribute) => ({
          key: attribute.key,
          value: parseAttributeValue(attribute),
        })),
      };
      const preview: TargetingBucketPreviewInput | undefined = bucketEnabled
        ? {
            resourceType,
            resourceKey,
            salt,
            allocations: allocations.map((allocation) => ({
              variant: allocation.variant,
              start: parseInteger(allocation.start, "Allocation start"),
              end: parseInteger(allocation.end, "Allocation end"),
            })),
          }
        : undefined;
      const result = await simulateTargeting(
        csrfToken,
        scope,
        segmentId,
        context,
        preview,
      );
      setSimulation(result);
      toast.success("Server-side targeting simulation completed.");
    } catch (error) {
      toast.error(targetingErrorMessage(error));
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
      data-hydrated={hydrated ? "true" : "false"}
      data-targeting-workspace
    >
      <fieldset className="contents" disabled={!hydrated}>
        <section className="overflow-hidden rounded-3xl border border-violet-300/10 bg-[linear-gradient(135deg,rgba(139,92,246,0.14),rgba(15,23,42,0.84)_55%,rgba(2,6,23,0.96))] p-6 sm:p-8">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <Badge variant="info">
                <SlidersHorizontal aria-hidden="true" className="size-3" />
                Engine v1 live
              </Badge>
              <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
                Targeting segments
              </h1>
              <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
                Build reusable typed audiences, inspect short-circuit traces, and
                preview deterministic allocations. Simulations always run on the
                server against the same engine used by Feature, Config, and Release.
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

        <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
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
          <CatalogPanel catalog={catalog.data} error={catalog.error} />
        </div>

        {!scope ? (
          <Card>
            <CardContent className="flex min-h-40 items-center justify-center pt-6 text-center text-sm text-slate-500">
              Select an active tenant, application, and environment to manage its
              targeting segments.
            </CardContent>
          </Card>
        ) : (
          <>
            <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
              <SegmentList
                data={segments.data?.segments ?? []}
                error={segments.error}
                includeArchived={includeArchived}
                isLoading={segments.isLoading}
                nextPageToken={segments.data?.nextPageToken ?? ""}
                onChangeStatus={changeStatus}
                onIncludeArchived={changeIncludeArchived}
                onInspect={inspectSegment}
                onNext={(token) => {
                  setPageTokens((current) => [...current.slice(0, pageIndex + 1), token]);
                  setPageIndex((current) => current + 1);
                }}
                onPrevious={() => setPageIndex((current) => Math.max(0, current - 1))}
                onQuery={changeQuery}
                pageIndex={pageIndex}
                pending={pending}
                query={query}
                selectedId={segmentId}
              />
              <CreateSegmentPanel
                catalog={catalog.data}
                description={createDescription}
                displayName={createName}
                isPending={pending === "create"}
                keyValue={createKey}
                onDescription={setCreateDescription}
                onDisplayName={setCreateName}
                onKey={setCreateKey}
                onRule={setCreateRule}
                onSubmit={submitCreate}
                rule={createRule}
              />
            </div>

            {selectedSegment && (
              <EditSegmentPanel
                catalog={catalog.data}
                description={editDescription}
                displayName={editName}
                isPending={pending === `update-${selectedSegment.id}`}
                onDescription={setEditDescription}
                onDisplayName={setEditName}
                onRule={setEditRule}
                onSubmit={submitUpdate}
                rule={editRule}
                segment={selectedSegment}
              />
            )}

            <SimulatorPanel
              allocations={allocations}
              attributes={attributes}
              bucketEnabled={bucketEnabled}
              clientVersion={clientVersion}
              language={language}
              onAddAllocation={() =>
                setAllocations((current) => [
                  ...current,
                  { variant: "", start: "", end: "" },
                ])
              }
              onAddAttribute={() =>
                setAttributes((current) => [
                  ...current,
                  { key: "", kind: "text", rawValue: "" },
                ])
              }
              onAllocation={setAllocations}
              onAttribute={setAttributes}
              onBucketEnabled={setBucketEnabled}
              onClientVersion={setClientVersion}
              onLanguage={setLanguage}
              onPlatform={setPlatform}
              onRegion={setRegion}
              onResourceKey={setResourceKey}
              onResourceType={setResourceType}
              onSalt={setSalt}
              onSubmit={submitSimulation}
              onTargetingKey={setTargetingKey}
              onUserId={setUserId}
              pending={pending === "simulate"}
              platform={platform}
              region={region}
              resourceKey={resourceKey}
              resourceType={resourceType}
              salt={salt}
              segmentId={segmentId}
              segments={segments.data?.segments ?? []}
              simulation={simulation}
              targetingKey={targetingKey}
              userId={userId}
            />
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
        <CardTitle>Environment scope</CardTitle>
        <CardDescription>
          Segments are isolated to one application environment.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-3">
        <label>
          <span className={labelClassName}>Tenant</span>
          <select
            aria-label="Targeting tenant"
            className={inputClassName}
            onChange={(event) => onTenant(event.target.value)}
            value={tenantId}
          >
            <option value="">Select tenant</option>
            {tenants.map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.displayName} ({tenant.slug})
              </option>
            ))}
          </select>
        </label>
        <label>
          <span className={labelClassName}>Application</span>
          <select
            aria-label="Targeting application"
            className={inputClassName}
            disabled={!tenantId}
            onChange={(event) => onApplication(event.target.value)}
            value={applicationId}
          >
            <option value="">Select application</option>
            {applications.map((application) => (
              <option key={application.id} value={application.id}>
                {application.displayName} ({application.slug})
              </option>
            ))}
          </select>
        </label>
        <label>
          <span className={labelClassName}>Environment</span>
          <select
            aria-label="Targeting environment"
            className={inputClassName}
            disabled={!applicationId}
            onChange={(event) => onEnvironment(event.target.value)}
            value={environmentId}
          >
            <option value="">Select environment</option>
            {environments.map((environment) => (
              <option key={environment.id} value={environment.id}>
                {environment.displayName} ({environment.slug})
              </option>
            ))}
          </select>
        </label>
      </CardContent>
    </Card>
  );
}

function CatalogPanel({
  catalog,
  error,
}: {
  catalog?: TargetingCatalog;
  error?: unknown;
}) {
  return (
    <Card data-ui-action="list-targeting-attributes">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Braces aria-hidden="true" className="size-4 text-violet-300" />
          Evaluation contract
        </CardTitle>
        <CardDescription>
          Authoritative built-ins, operators, and bucketing limits.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {error ? (
          <InlineError message={targetingErrorMessage(error)} />
        ) : !catalog ? (
          <LoadingLabel />
        ) : (
          <div className="space-y-4">
            <div className="flex flex-wrap gap-2">
              {catalog.attributes.map((attribute) => (
                <span
                  className="rounded-lg border border-white/8 bg-white/[0.03] px-2.5 py-1.5 font-mono text-[11px] text-slate-300"
                  key={attribute.key}
                >
                  {attribute.key}
                  {attribute.required ? " *" : ""}
                </span>
              ))}
            </div>
            <div className="grid grid-cols-3 gap-3 text-center text-xs">
              <Metric label="Operators" value={catalog.operators.length} />
              <Metric label="Max conditions" value={catalog.maximumConditions} />
              <Metric label={`Buckets ${catalog.bucketingVersion}`} value={catalog.bucketCount} />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function SegmentList({
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
  data: TargetingSegmentRecord[];
  error?: unknown;
  includeArchived: boolean;
  isLoading: boolean;
  nextPageToken: string;
  onChangeStatus: (segment: TargetingSegmentRecord, restore: boolean) => void;
  onIncludeArchived: (value: boolean) => void;
  onInspect: (segment: TargetingSegmentRecord) => void;
  onNext: (token: string) => void;
  onPrevious: () => void;
  onQuery: (value: string) => void;
  pageIndex: number;
  pending: string;
  query: string;
  selectedId: string;
}) {
  return (
    <Card data-ui-action="list-segments">
      <CardHeader>
        <CardTitle>Segment inventory</CardTitle>
        <CardDescription>
          Search, inspect, archive, and restore reusable audiences.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
          <label className="relative block">
            <Search
              aria-hidden="true"
              className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-600"
            />
            <input
              aria-label="Search targeting segments"
              className={cn(inputClassName, "pl-9")}
              onChange={(event) => onQuery(event.target.value)}
              placeholder="Search key or name"
              value={query}
            />
          </label>
          <label className="flex items-center gap-2 text-xs text-slate-400">
            <input
              checked={includeArchived}
              onChange={(event) => onIncludeArchived(event.target.checked)}
              type="checkbox"
            />
            Include archived segments
          </label>
        </div>

        {error ? (
          <InlineError message={targetingErrorMessage(error)} />
        ) : isLoading && data.length === 0 ? (
          <LoadingLabel />
        ) : data.length === 0 ? (
          <p className="rounded-xl border border-dashed border-white/10 p-8 text-center text-sm text-slate-500">
            No segments match this scope and filter.
          </p>
        ) : (
          <div className="space-y-2">
            {data.map((segment) => {
              const active = segment.status === activeStatus;
              return (
                <article
                  className={cn(
                    "rounded-xl border p-4 transition",
                    selectedId === segment.id
                      ? "border-violet-400/35 bg-violet-400/[0.07]"
                      : "border-white/8 bg-white/[0.025]",
                  )}
                  data-testid={`targeting-segment-${segment.key}`}
                  key={segment.id}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex items-center gap-2">
                        <p className="font-medium text-slate-100">{segment.displayName}</p>
                        <Badge variant={active ? "success" : "planned"}>
                          {active ? "Active" : "Archived"}
                        </Badge>
                      </div>
                      <p className="mt-1 font-mono text-xs text-violet-300">
                        {segment.key}
                      </p>
                      <p className="mt-2 text-xs text-slate-500">
                        {segment.rule.conditions.length} condition
                        {segment.rule.conditions.length === 1 ? "" : "s"} · v
                        {segment.version}
                      </p>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        data-ui-action="get-segment"
                        disabled={pending === `get-${segment.id}`}
                        onClick={() => void onInspect(segment)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {pending === `get-${segment.id}` ? (
                          <LoaderCircle className="size-3.5 animate-spin" />
                        ) : (
                          <SlidersHorizontal className="size-3.5" />
                        )}
                        Inspect
                      </Button>
                      {active ? (
                        <Button
                          aria-label={`Archive ${segment.displayName}`}
                          data-ui-action="archive-segment"
                          disabled={pending === `archive-${segment.id}`}
                          onClick={() => void onChangeStatus(segment, false)}
                          size="sm"
                          type="button"
                          variant="ghost"
                        >
                          <Archive className="size-3.5" />
                        </Button>
                      ) : (
                        <Button
                          data-ui-action="restore-segment"
                          disabled={pending === `restore-${segment.id}`}
                          onClick={() => void onChangeStatus(segment, true)}
                          size="sm"
                          type="button"
                          variant="ghost"
                        >
                          <RotateCcw className="size-3.5" />
                          Restore
                        </Button>
                      )}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}

        <div className="flex items-center justify-between border-t border-white/8 pt-4">
          <span className="text-xs text-slate-500">Page {pageIndex + 1}</span>
          <div className="flex gap-2">
            <Button
              aria-label="Previous segment page"
              disabled={pageIndex === 0}
              onClick={onPrevious}
              size="sm"
              type="button"
              variant="outline"
            >
              <ChevronLeft className="size-3.5" />
            </Button>
            <Button
              aria-label="Next segment page"
              disabled={!nextPageToken}
              onClick={() => onNext(nextPageToken)}
              size="sm"
              type="button"
              variant="outline"
            >
              <ChevronRight className="size-3.5" />
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function CreateSegmentPanel({
  catalog,
  description,
  displayName,
  isPending,
  keyValue,
  onDescription,
  onDisplayName,
  onKey,
  onRule,
  onSubmit,
  rule,
}: {
  catalog?: TargetingCatalog;
  description: string;
  displayName: string;
  isPending: boolean;
  keyValue: string;
  onDescription: (value: string) => void;
  onDisplayName: (value: string) => void;
  onKey: (value: string) => void;
  onRule: (value: TargetingRuleDraft) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  rule: TargetingRuleDraft;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Create segment</CardTitle>
        <CardDescription>
          Keys are immutable; rules remain editable with optimistic concurrency.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="space-y-4"
          data-ui-action="create-segment"
          onSubmit={onSubmit}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <label>
              <span className={labelClassName}>Segment key</span>
              <input
                className={inputClassName}
                name="segmentKey"
                onChange={(event) => onKey(event.target.value)}
                placeholder="early-access"
                required
                value={keyValue}
              />
            </label>
            <label>
              <span className={labelClassName}>Display name</span>
              <input
                className={inputClassName}
                name="segmentDisplayName"
                onChange={(event) => onDisplayName(event.target.value)}
                required
                value={displayName}
              />
            </label>
          </div>
          <label className="block">
            <span className={labelClassName}>Description</span>
            <textarea
              className={cn(inputClassName, "h-20 resize-y py-2")}
              name="segmentDescription"
              onChange={(event) => onDescription(event.target.value)}
              value={description}
            />
          </label>
          <RuleEditor
            catalog={catalog}
            draft={rule}
            idPrefix="create"
            onChange={onRule}
          />
          <Button disabled={isPending} type="submit">
            {isPending ? (
              <LoaderCircle className="size-4 animate-spin" />
            ) : (
              <Plus className="size-4" />
            )}
            Create segment
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function EditSegmentPanel({
  catalog,
  description,
  displayName,
  isPending,
  onDescription,
  onDisplayName,
  onRule,
  onSubmit,
  rule,
  segment,
}: {
  catalog?: TargetingCatalog;
  description: string;
  displayName: string;
  isPending: boolean;
  onDescription: (value: string) => void;
  onDisplayName: (value: string) => void;
  onRule: (value: TargetingRuleDraft) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  rule: TargetingRuleDraft;
  segment: TargetingSegmentRecord;
}) {
  const active = segment.status === activeStatus;
  return (
    <Card>
      <CardHeader className="sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>Selected segment · {segment.key}</CardTitle>
          <CardDescription>
            Loaded through GetSegment · version {segment.version}
          </CardDescription>
        </div>
        <Badge variant={active ? "success" : "planned"}>
          {active ? "Active" : "Archived"}
        </Badge>
      </CardHeader>
      <CardContent>
        <form
          className="space-y-4"
          data-ui-action="update-segment"
          onSubmit={onSubmit}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <label>
              <span className={labelClassName}>Display name</span>
              <input
                className={inputClassName}
                name="editSegmentDisplayName"
                onChange={(event) => onDisplayName(event.target.value)}
                required
                value={displayName}
              />
            </label>
            <label>
              <span className={labelClassName}>Immutable key</span>
              <input className={inputClassName} disabled value={segment.key} />
            </label>
          </div>
          <label className="block">
            <span className={labelClassName}>Description</span>
            <textarea
              className={cn(inputClassName, "h-20 resize-y py-2")}
              name="editSegmentDescription"
              onChange={(event) => onDescription(event.target.value)}
              value={description}
            />
          </label>
          <RuleEditor
            catalog={catalog}
            draft={rule}
            idPrefix="edit"
            onChange={onRule}
          />
          <Button disabled={!active || isPending} type="submit">
            {isPending ? (
              <LoaderCircle className="size-4 animate-spin" />
            ) : (
              <Save className="size-4" />
            )}
            Save segment
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function SimulatorPanel({
  allocations,
  attributes,
  bucketEnabled,
  clientVersion,
  language,
  onAddAllocation,
  onAddAttribute,
  onAllocation,
  onAttribute,
  onBucketEnabled,
  onClientVersion,
  onLanguage,
  onPlatform,
  onRegion,
  onResourceKey,
  onResourceType,
  onSalt,
  onSubmit,
  onTargetingKey,
  onUserId,
  pending,
  platform,
  region,
  resourceKey,
  resourceType,
  salt,
  segmentId,
  segments,
  simulation,
  targetingKey,
  userId,
}: {
  allocations: AllocationDraft[];
  attributes: AttributeDraft[];
  bucketEnabled: boolean;
  clientVersion: string;
  language: string;
  onAddAllocation: () => void;
  onAddAttribute: () => void;
  onAllocation: (value: AllocationDraft[]) => void;
  onAttribute: (value: AttributeDraft[]) => void;
  onBucketEnabled: (value: boolean) => void;
  onClientVersion: (value: string) => void;
  onLanguage: (value: string) => void;
  onPlatform: (value: string) => void;
  onRegion: (value: string) => void;
  onResourceKey: (value: string) => void;
  onResourceType: (value: string) => void;
  onSalt: (value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onTargetingKey: (value: string) => void;
  onUserId: (value: string) => void;
  pending: boolean;
  platform: string;
  region: string;
  resourceKey: string;
  resourceType: string;
  salt: string;
  segmentId: string;
  segments: TargetingSegmentRecord[];
  simulation?: TargetingSimulationResult;
  targetingKey: string;
  userId: string;
}) {
  const selectSegment = useTargetingSelection((state) => state.selectSegment);
  return (
    <Card data-ui-action="simulate-targeting">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <FlaskConical aria-hidden="true" className="size-4 text-violet-300" />
          Server-side simulator
        </CardTitle>
        <CardDescription>
          Context values are sent to the authoritative engine and are never logged.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-5" onSubmit={onSubmit}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <label className="sm:col-span-2">
              <span className={labelClassName}>Segment</span>
              <select
                aria-label="Simulation segment"
                className={inputClassName}
                onChange={(event) => selectSegment(event.target.value)}
                required
                value={segmentId}
              >
                <option value="">Select segment</option>
                {segments.map((segment) => (
                  <option key={segment.id} value={segment.id}>
                    {segment.displayName} ({segment.key})
                  </option>
                ))}
              </select>
            </label>
            <TextField
              label="Targeting key"
              name="simulationTargetingKey"
              onChange={onTargetingKey}
              required
              value={targetingKey}
            />
            <TextField
              label="User ID"
              name="simulationUserId"
              onChange={onUserId}
              value={userId}
            />
            <TextField
              label="Client version"
              name="simulationClientVersion"
              onChange={onClientVersion}
              value={clientVersion}
            />
            <TextField
              label="Platform"
              name="simulationPlatform"
              onChange={onPlatform}
              value={platform}
            />
            <TextField
              label="Region"
              name="simulationRegion"
              onChange={onRegion}
              value={region}
            />
            <TextField
              label="Language"
              name="simulationLanguage"
              onChange={onLanguage}
              value={language}
            />
          </div>

          <div className="rounded-xl border border-white/8 bg-white/[0.025] p-4">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-sm font-medium text-slate-200">Custom attributes</p>
                <p className="mt-1 text-xs text-slate-500">
                  Typed non-PII attributes only; built-ins belong in the fields above.
                </p>
              </div>
              <Button onClick={onAddAttribute} size="sm" type="button" variant="outline">
                <Plus className="size-3.5" /> Add attribute
              </Button>
            </div>
            <div className="mt-3 space-y-2">
              {attributes.map((attribute, index) => (
                <div
                  className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_9rem_minmax(0,1fr)_auto]"
                  key={index}
                >
                  <input
                    aria-label={`Custom attribute ${index + 1} name`}
                    className={inputClassName}
                    onChange={(event) =>
                      onAttribute(
                        replaceAt(attributes, index, {
                          ...attribute,
                          key: event.target.value,
                        }),
                      )
                    }
                    placeholder="subscription.plan"
                    value={attribute.key}
                  />
                  <select
                    aria-label={`Custom attribute ${index + 1} type`}
                    className={inputClassName}
                    onChange={(event) =>
                      onAttribute(
                        replaceAt(attributes, index, {
                          ...attribute,
                          kind: event.target.value as AttributeDraft["kind"],
                        }),
                      )
                    }
                    value={attribute.kind}
                  >
                    <option value="text">Text</option>
                    <option value="truth">Boolean</option>
                    <option value="numeric">Number</option>
                  </select>
                  <input
                    aria-label={`Custom attribute ${index + 1} value`}
                    className={inputClassName}
                    onChange={(event) =>
                      onAttribute(
                        replaceAt(attributes, index, {
                          ...attribute,
                          rawValue: event.target.value,
                        }),
                      )
                    }
                    value={attribute.rawValue}
                  />
                  <Button
                    aria-label={`Remove custom attribute ${index + 1}`}
                    onClick={() =>
                      onAttribute(attributes.filter((_, candidate) => candidate !== index))
                    }
                    size="sm"
                    type="button"
                    variant="ghost"
                  >
                    <Trash2 className="size-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-xl border border-white/8 bg-white/[0.025] p-4">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <input
                checked={bucketEnabled}
                onChange={(event) => onBucketEnabled(event.target.checked)}
                type="checkbox"
              />
              Preview deterministic bucket allocation
            </label>
            {bucketEnabled && (
              <div className="mt-4 space-y-3">
                <div className="grid gap-3 sm:grid-cols-3">
                  <TextField
                    label="Resource type"
                    name="simulationResourceType"
                    onChange={onResourceType}
                    required
                    value={resourceType}
                  />
                  <TextField
                    label="Resource key"
                    name="simulationResourceKey"
                    onChange={onResourceKey}
                    required
                    value={resourceKey}
                  />
                  <TextField
                    label="Stable salt"
                    name="simulationSalt"
                    onChange={onSalt}
                    value={salt}
                  />
                </div>
                {allocations.map((allocation, index) => (
                  <div
                    className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_8rem_8rem_auto]"
                    key={index}
                  >
                    <input
                      aria-label={`Allocation ${index + 1} variant`}
                      className={inputClassName}
                      onChange={(event) =>
                        onAllocation(
                          replaceAt(allocations, index, {
                            ...allocation,
                            variant: event.target.value,
                          }),
                        )
                      }
                      placeholder="enabled"
                      value={allocation.variant}
                    />
                    <input
                      aria-label={`Allocation ${index + 1} start`}
                      className={inputClassName}
                      min="0"
                      onChange={(event) =>
                        onAllocation(
                          replaceAt(allocations, index, {
                            ...allocation,
                            start: event.target.value,
                          }),
                        )
                      }
                      type="number"
                      value={allocation.start}
                    />
                    <input
                      aria-label={`Allocation ${index + 1} end`}
                      className={inputClassName}
                      max="100000"
                      onChange={(event) =>
                        onAllocation(
                          replaceAt(allocations, index, {
                            ...allocation,
                            end: event.target.value,
                          }),
                        )
                      }
                      type="number"
                      value={allocation.end}
                    />
                    <Button
                      aria-label={`Remove allocation ${index + 1}`}
                      disabled={allocations.length === 1}
                      onClick={() =>
                        onAllocation(
                          allocations.filter((_, candidate) => candidate !== index),
                        )
                      }
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <Trash2 className="size-3.5" />
                    </Button>
                  </div>
                ))}
                <Button onClick={onAddAllocation} size="sm" type="button" variant="outline">
                  <Plus className="size-3.5" /> Add allocation
                </Button>
              </div>
            )}
          </div>

          <Button disabled={!segmentId || pending} type="submit">
            {pending ? (
              <LoaderCircle className="size-4 animate-spin" />
            ) : (
              <FlaskConical className="size-4" />
            )}
            Simulate on server
          </Button>
        </form>

        {simulation && <SimulationResult result={simulation} />}
      </CardContent>
    </Card>
  );
}

function SimulationResult({ result }: { result: TargetingSimulationResult }) {
  return (
    <div className="mt-6 rounded-2xl border border-violet-400/20 bg-violet-400/[0.06] p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="grid size-10 place-items-center rounded-xl bg-violet-400/10 text-violet-300">
            <Gauge className="size-5" />
          </div>
          <div>
            <p className="font-semibold text-white">
              {result.matched ? "Segment matched" : "Segment did not match"}
            </p>
            <p className="text-xs text-slate-500">
              {result.segmentKey} · version {result.segmentVersion}
            </p>
          </div>
        </div>
        <Badge variant={result.matched ? "success" : "planned"}>
          {result.reason.replaceAll("_", " ")}
        </Badge>
      </div>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        {result.conditionTraces.map((trace) => (
          <div className="rounded-lg border border-white/8 bg-black/10 p-3" key={trace.conditionId}>
            <p className="font-mono text-xs text-slate-300">{trace.conditionId}</p>
            <p className="mt-1 text-xs text-slate-500">
              {trace.matched ? "Matched" : "Not matched"} · {prettyReason(trace.reason)}
            </p>
          </div>
        ))}
      </div>
      {result.bucketEvaluated && (
        <div className="mt-4 rounded-lg border border-white/8 bg-black/10 p-3 text-xs text-slate-400">
          Bucket <span className="font-mono text-violet-300">{result.bucket}</span>
          {result.selectedVariant ? (
            <>
              {" "}selected variant{" "}
              <span className="font-mono text-violet-300">
                {result.selectedVariant}
              </span>
            </>
          ) : (
            " has no allocation"
          )}
          <p className="mt-1 break-all font-mono text-[10px] text-slate-600">
            {result.bucketNamespace} · {result.bucketingVersion}
          </p>
        </div>
      )}
    </div>
  );
}

function TextField({
  label,
  name,
  onChange,
  required = false,
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  required?: boolean;
  value: string;
}) {
  return (
    <label>
      <span className={labelClassName}>{label}</span>
      <input
        className={inputClassName}
        name={name}
        onChange={(event) => onChange(event.target.value)}
        required={required}
        value={value}
      />
    </label>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-white/8 bg-white/[0.025] p-3">
      <p className="text-lg font-semibold text-white">{value.toLocaleString()}</p>
      <p className="mt-1 text-[10px] uppercase tracking-wider text-slate-600">{label}</p>
    </div>
  );
}

function InlineError({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-2 rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-3 text-sm text-rose-200">
      <CircleAlert className="mt-0.5 size-4 shrink-0" />
      <span>{message}</span>
    </div>
  );
}

function LoadingLabel() {
  return (
    <p className="flex items-center gap-2 text-sm text-slate-500">
      <LoaderCircle className="size-4 animate-spin" /> Loading…
    </p>
  );
}

function parseAttributeValue(attribute: AttributeDraft): TargetingValueInput {
  if (attribute.kind === "text") {
    return { text: attribute.rawValue };
  }
  if (attribute.kind === "truth") {
    const normalized = attribute.rawValue.trim().toLowerCase();
    if (normalized !== "true" && normalized !== "false") {
      throw new Error(`Attribute ${attribute.key || "value"} requires true or false.`);
    }
    return { truth: normalized === "true" };
  }
  const numeric = Number(attribute.rawValue);
  if (!Number.isFinite(numeric) || attribute.rawValue.trim().length === 0) {
    throw new Error(`Attribute ${attribute.key || "value"} requires a finite number.`);
  }
  return { numeric };
}

function parseInteger(value: string, label: string): number {
  const parsed = Number(value);
  if (!Number.isInteger(parsed)) {
    throw new Error(`${label} must be an integer.`);
  }
  return parsed;
}

function replaceAt<T>(values: T[], index: number, value: T): T[] {
  return values.map((candidate, candidateIndex) =>
    candidateIndex === index ? value : candidate,
  );
}

function prettyReason(value: string): string {
  return value
    .replace("TARGETING_CONDITION_REASON_", "")
    .toLowerCase()
    .replaceAll("_", " ");
}
