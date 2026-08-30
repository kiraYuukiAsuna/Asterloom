"use client";

import {
  Archive,
  Boxes,
  Building2,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  CloudCog,
  Edit3,
  LoaderCircle,
  LockKeyhole,
  Plus,
  RotateCcw,
  Search,
  Trash2,
  UserPlus,
  Users,
} from "lucide-react";
import { type FormEvent, type ReactNode, useState } from "react";
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
  archiveApplication,
  archiveEnvironment,
  archiveTenant,
  createApplication,
  createEnvironment,
  createTenant,
  isVersionConflict,
  listApplications,
  listEnvironments,
  listTenantMemberships,
  listTenants,
  platformErrorMessage,
  removeTenantMembership,
  restoreApplication,
  restoreEnvironment,
  restoreTenant,
  setTenantMembership,
  updateApplication,
  updateEnvironment,
  updateTenant,
  type ApplicationRecord,
  type EnvironmentRecord,
  type EnvironmentType,
  type TenantMembershipRecord,
  type TenantRecord,
} from "@/lib/api/platform-management";
import { cn } from "@/lib/utils/cn";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const activeStatus = "RESOURCE_STATUS_ACTIVE";
const activeMembership = "MEMBERSHIP_STATUS_ACTIVE";

type MutationRunner = (
  key: string,
  work: () => Promise<unknown>,
  refresh: Array<() => Promise<unknown>>,
  successMessage: string,
) => Promise<boolean>;

export function PlatformWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const [pending, setPending] = useState("");
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [selectedApplicationId, setSelectedApplicationId] = useState("");
  const [tenantQuery, setTenantQuery] = useState("");
  const [applicationQuery, setApplicationQuery] = useState("");
  const [environmentQuery, setEnvironmentQuery] = useState("");
  const [includeArchivedTenants, setIncludeArchivedTenants] = useState(false);
  const [includeArchivedApplications, setIncludeArchivedApplications] =
    useState(false);
  const [includeArchivedEnvironments, setIncludeArchivedEnvironments] =
    useState(false);
  const [includeRemovedMemberships, setIncludeRemovedMemberships] =
    useState(false);
  const tenantCursor = usePageCursor();
  const applicationCursor = usePageCursor();
  const environmentCursor = usePageCursor();
  const membershipCursor = usePageCursor();

  const tenants = useSWR(
    [
      "platform-tenants",
      tenantQuery,
      includeArchivedTenants,
      tenantCursor.current,
    ],
    () =>
      listTenants({
        includeArchived: includeArchivedTenants,
        pageSize: 25,
        pageToken: tenantCursor.current,
        query: tenantQuery,
      }),
    { keepPreviousData: true },
  );
  const selectedTenant = tenants.data?.tenants.find(
    (tenant) => tenant.id === selectedTenantId,
  );

  const applications = useSWR(
    selectedTenantId
      ? [
          "platform-applications",
          selectedTenantId,
          applicationQuery,
          includeArchivedApplications,
          applicationCursor.current,
        ]
      : null,
    () =>
      listApplications(selectedTenantId, {
        includeArchived: includeArchivedApplications,
        pageSize: 25,
        pageToken: applicationCursor.current,
        query: applicationQuery,
      }),
    { keepPreviousData: true },
  );
  const selectedApplication = applications.data?.applications.find(
    (application) => application.id === selectedApplicationId,
  );

  const environments = useSWR(
    selectedTenantId && selectedApplicationId
      ? [
          "platform-environments",
          selectedTenantId,
          selectedApplicationId,
          environmentQuery,
          includeArchivedEnvironments,
          environmentCursor.current,
        ]
      : null,
    () =>
      listEnvironments(selectedTenantId, selectedApplicationId, {
        includeArchived: includeArchivedEnvironments,
        pageSize: 25,
        pageToken: environmentCursor.current,
        query: environmentQuery,
      }),
    { keepPreviousData: true },
  );

  const memberships = useSWR(
    selectedTenantId
      ? [
          "platform-memberships",
          selectedTenantId,
          includeRemovedMemberships,
          membershipCursor.current,
        ]
      : null,
    () =>
      listTenantMemberships(selectedTenantId, {
        includeRemoved: includeRemovedMemberships,
        pageSize: 25,
        pageToken: membershipCursor.current,
      }),
    { keepPreviousData: true },
  );

  const runMutation: MutationRunner = async (
    key,
    work,
    refresh,
    successMessage,
  ) => {
    setPending(key);
    try {
      await work();
      await Promise.all(refresh.map((reload) => reload()));
      toast.success(translate(successMessage));
      return true;
    } catch (error) {
      await Promise.allSettled(refresh.map((reload) => reload()));
      toast.error(
        translate(isVersionConflict(error)
          ? "This resource changed in another session. Latest data loaded; review it and retry."
          : platformErrorMessage(error)),
      );
      return false;
    } finally {
      setPending("");
    }
  };

  function selectTenant(tenant: TenantRecord) {
    setSelectedTenantId(tenant.id);
    setSelectedApplicationId("");
    setApplicationQuery("");
    setEnvironmentQuery("");
    applicationCursor.reset();
    environmentCursor.reset();
    membershipCursor.reset();
  }

  function selectApplication(application: ApplicationRecord) {
    setSelectedApplicationId(application.id);
    setEnvironmentQuery("");
    environmentCursor.reset();
  }

  return (
    <div
      className="space-y-6"
      data-hydrated={hydrated ? "true" : "false"}
      data-platform-workspace
    >
      <fieldset className="contents" disabled={!hydrated}>
      <section className="theme-hero-sky overflow-hidden rounded-3xl border border-sky-300/10 bg-[linear-gradient(135deg,rgba(14,165,233,0.12),rgba(15,23,42,0.82)_55%,rgba(2,6,23,0.96))] p-6 sm:p-8">
        <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <Badge variant="info">
              <CloudCog aria-hidden="true" className="size-3" />
              {translate("Foundation live")}</Badge>
            <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
              {translate("Platform resource workspace")}</h1>
            <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
              {translate("Model each product from tenant to application and environment, then control who can operate inside that boundary. Every action below is backed by the versioned PlatformAdminService contract.")}</p>
          </div>
          <div className="flex gap-2 text-xs text-slate-500">
            <ScopeCrumb active={!selectedTenant}>{translate("Tenants")}</ScopeCrumb>
            <span>/</span>
            <ScopeCrumb active={Boolean(selectedTenant && !selectedApplication)}>
              {selectedTenant?.slug ?? "Application"}
            </ScopeCrumb>
            <span>/</span>
            <ScopeCrumb active={Boolean(selectedApplication)}>
              {selectedApplication?.slug ?? "Environment"}
            </ScopeCrumb>
          </div>
        </div>
      </section>

      <div className="grid items-start gap-5 xl:grid-cols-3">
        <TenantPanel
          csrfToken={csrfToken}
          cursor={tenantCursor}
          includeArchived={includeArchivedTenants}
          onIncludeArchivedChange={(value) => {
            tenantCursor.reset();
            setIncludeArchivedTenants(value);
          }}
          onQueryChange={(value) => {
            tenantCursor.reset();
            setTenantQuery(value);
          }}
          onSelect={selectTenant}
          page={tenants.data}
          pending={pending}
          query={tenantQuery}
          reload={() => tenants.mutate()}
          runMutation={runMutation}
          selectedId={selectedTenantId}
          state={tenants}
        />

        <ApplicationPanel
          csrfToken={csrfToken}
          cursor={applicationCursor}
          includeArchived={includeArchivedApplications}
          onIncludeArchivedChange={(value) => {
            applicationCursor.reset();
            setIncludeArchivedApplications(value);
          }}
          onQueryChange={(value) => {
            applicationCursor.reset();
            setApplicationQuery(value);
          }}
          onSelect={selectApplication}
          page={applications.data}
          pending={pending}
          query={applicationQuery}
          reload={() => applications.mutate()}
          runMutation={runMutation}
          selectedId={selectedApplicationId}
          tenant={selectedTenant}
        />

        <EnvironmentPanel
          application={selectedApplication}
          csrfToken={csrfToken}
          cursor={environmentCursor}
          includeArchived={includeArchivedEnvironments}
          onIncludeArchivedChange={(value) => {
            environmentCursor.reset();
            setIncludeArchivedEnvironments(value);
          }}
          onQueryChange={(value) => {
            environmentCursor.reset();
            setEnvironmentQuery(value);
          }}
          page={environments.data}
          pending={pending}
          query={environmentQuery}
          reload={() => environments.mutate()}
          runMutation={runMutation}
          tenant={selectedTenant}
        />
      </div>

      <MembershipPanel
        csrfToken={csrfToken}
        cursor={membershipCursor}
        includeRemoved={includeRemovedMemberships}
        onIncludeRemovedChange={(value) => {
          membershipCursor.reset();
          setIncludeRemovedMemberships(value);
        }}
        page={memberships.data}
        pending={pending}
        reload={() => memberships.mutate()}
        runMutation={runMutation}
        tenant={selectedTenant}
      />
      </fieldset>
    </div>
  );
}

type Cursor = ReturnType<typeof usePageCursor>;
type SwrState = {
  error?: unknown;
  isLoading: boolean;
};

function TenantPanel({
  csrfToken,
  cursor,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  onSelect,
  page,
  pending,
  query,
  reload,
  runMutation,
  selectedId,
  state,
}: {
  csrfToken: string;
  cursor: Cursor;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  onSelect: (tenant: TenantRecord) => void;
  page?: { nextPageToken?: string | null; tenants: TenantRecord[] };
  pending: string;
  query: string;
  reload: () => Promise<unknown>;
  runMutation: MutationRunner;
  selectedId: string;
  state: SwrState;
}) {
  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [editingId, setEditingId] = useState("");
  const [editingName, setEditingName] = useState("");

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    let created: TenantRecord | undefined;
    const ok = await runMutation(
      "tenant-create",
      async () => {
        created = await createTenant(csrfToken, { displayName, slug });
      },
      [reload],
      "Tenant created.",
    );
    if (ok && created) {
      setSlug("");
      setDisplayName("");
      onQueryChange(created.slug);
      onSelect(created);
    }
  }

  async function submitEdit(event: FormEvent, tenant: TenantRecord) {
    event.preventDefault();
    const ok = await runMutation(
      "tenant-update-" + tenant.id,
      () => updateTenant(csrfToken, tenant, editingName),
      [reload],
      "Tenant updated.",
    );
    if (ok) setEditingId("");
  }

  return (
    <Card className="min-w-0" data-ui-action="list-tenants">
      <PanelHeader
        description={translate("Top-level isolation and ownership boundary.")}
        icon={Building2}
        title={translate("Tenants")}
      />
      <CardContent className="space-y-4">
        <FilterBar
          checked={includeArchived}
          checkedLabel="Archived"
          checkboxName="include-archived-tenants"
          onCheckedChange={onIncludeArchivedChange}
          onQueryChange={onQueryChange}
          placeholder={translate("Search tenants…")}
          query={query}
        />
        <ResourceListState
          emptyMessage={translate("No tenants match this view.")}
          error={state.error}
          isLoading={state.isLoading}
          items={page?.tenants}
        >
          {(page?.tenants ?? []).map((tenant) => (
            <ResourceRow
              active={selectedId === tenant.id}
              key={tenant.id}
              onSelect={() => onSelect(tenant)}
              slug={tenant.slug}
              status={tenant.status}
              testId={"tenant-" + tenant.slug}
              title={tenant.displayName}
            >
              {editingId === tenant.id ? (
                <form
                  className="mt-3 flex gap-2"
                  onSubmit={(event) => submitEdit(event, tenant)}
                >
                  <label className="sr-only" htmlFor={"tenant-name-" + tenant.id}>
                    {translate("Tenant display name")}</label>
                  <input
                    autoFocus
                    className={inputClassName}
                    id={"tenant-name-" + tenant.id}
                    onChange={(event) => setEditingName(event.target.value)}
                    value={editingName}
                  />
                  <Button
                    data-ui-action="update-tenant"
                    disabled={pending !== ""}
                    size="sm"
                    type="submit"
                  >
                    {translate("Save")}</Button>
                </form>
              ) : (
                <RowActions>
                  <Button
                    aria-label={"Edit tenant " + tenant.displayName}
                    onClick={() => {
                      setEditingId(tenant.id);
                      setEditingName(tenant.displayName);
                    }}
                    size="sm"
                    type="button"
                    variant="ghost"
                  >
                    <Edit3 aria-hidden="true" className="size-3.5" />
                    {translate("Edit")}</Button>
                  {tenant.status === activeStatus ? (
                    <Button
                      className="text-rose-300 hover:text-rose-200"
                      data-ui-action="archive-tenant"
                      disabled={pending !== ""}
                      onClick={() => {
                        if (!window.confirm(translate(`Archive tenant ${tenant.displayName}?`))) return;
                        void runMutation(
                          "tenant-archive-" + tenant.id,
                          () => archiveTenant(csrfToken, tenant),
                          [reload],
                          "Tenant archived.",
                        );
                      }}
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <Archive aria-hidden="true" className="size-3.5" />
                      {translate("Archive")}</Button>
                  ) : (
                    <Button
                      data-ui-action="restore-tenant"
                      disabled={pending !== ""}
                      onClick={() =>
                        void runMutation(
                          "tenant-restore-" + tenant.id,
                          () => restoreTenant(csrfToken, tenant),
                          [reload],
                          "Tenant restored.",
                        )
                      }
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <RotateCcw aria-hidden="true" className="size-3.5" />
                      {translate("Restore")}</Button>
                  )}
                </RowActions>
              )}
            </ResourceRow>
          ))}
        </ResourceListState>
        <Pagination
          cursor={cursor}
          nextPageToken={page?.nextPageToken}
        />
        <CreateFormShell onSubmit={submitCreate} title={translate("Create tenant")}>
          <FormField label={translate("Slug")} name="tenant-slug">
            <input
              className={inputClassName}
              id="tenant-slug"
              name="tenant-slug"
              onChange={(event) => setSlug(event.target.value)}
              placeholder={translate("acme-labs")}
              value={slug}
            />
          </FormField>
          <FormField label={translate("Display name")} name="tenant-display-name">
            <input
              className={inputClassName}
              id="tenant-display-name"
              name="tenant-display-name"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder={translate("Acme Labs")}
              value={displayName}
            />
          </FormField>
          <Button
            className="w-full"
            data-ui-action="create-tenant"
            disabled={pending !== ""}
            type="submit"
          >
            <PendingIcon pending={pending === "tenant-create"} />
            {translate("Create tenant")}</Button>
        </CreateFormShell>
      </CardContent>
    </Card>
  );
}

function ApplicationPanel({
  csrfToken,
  cursor,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  onSelect,
  page,
  pending,
  query,
  reload,
  runMutation,
  selectedId,
  tenant,
}: {
  csrfToken: string;
  cursor: Cursor;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  onSelect: (application: ApplicationRecord) => void;
  page?: { applications: ApplicationRecord[]; nextPageToken?: string | null };
  pending: string;
  query: string;
  reload: () => Promise<unknown>;
  runMutation: MutationRunner;
  selectedId: string;
  tenant?: TenantRecord;
}) {
  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [editingId, setEditingId] = useState("");
  const [editingName, setEditingName] = useState("");

  if (!tenant) {
    return (
      <SelectionPlaceholder
        description={translate("Choose a tenant to load its application catalog.")}
        icon={Boxes}
        title={translate("Applications")}
      />
    );
  }
  const selectedTenant = tenant;

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    let created: ApplicationRecord | undefined;
    const ok = await runMutation(
      "application-create",
      async () => {
        created = await createApplication(csrfToken, selectedTenant.id, {
          displayName,
          slug,
        });
      },
      [reload],
      "Application created.",
    );
    if (ok && created) {
      setSlug("");
      setDisplayName("");
      onQueryChange(created.slug);
      onSelect(created);
    }
  }

  async function submitEdit(event: FormEvent, application: ApplicationRecord) {
    event.preventDefault();
    const ok = await runMutation(
      "application-update-" + application.id,
      () => updateApplication(csrfToken, application, editingName),
      [reload],
      "Application updated.",
    );
    if (ok) setEditingId("");
  }

  return (
    <Card className="min-w-0" data-ui-action="list-applications">
      <PanelHeader
        description={translate(`Application catalog inside ${tenant.displayName}.`)}
        icon={Boxes}
        title={translate("Applications")}
      />
      <CardContent className="space-y-4">
        <FilterBar
          checked={includeArchived}
          checkedLabel="Archived"
          checkboxName="include-archived-applications"
          onCheckedChange={onIncludeArchivedChange}
          onQueryChange={onQueryChange}
          placeholder={translate("Search applications…")}
          query={query}
        />
        <ResourceListState
          emptyMessage={translate("No applications match this view.")}
          isLoading={!page}
          items={page?.applications}
        >
          {(page?.applications ?? []).map((application) => (
            <ResourceRow
              active={selectedId === application.id}
              key={application.id}
              onSelect={() => onSelect(application)}
              slug={application.slug}
              status={application.status}
              testId={"application-" + application.slug}
              title={application.displayName}
            >
              {editingId === application.id ? (
                <form
                  className="mt-3 flex gap-2"
                  onSubmit={(event) => submitEdit(event, application)}
                >
                  <label
                    className="sr-only"
                    htmlFor={"application-name-" + application.id}
                  >
                    {translate("Application display name")}</label>
                  <input
                    autoFocus
                    className={inputClassName}
                    id={"application-name-" + application.id}
                    onChange={(event) => setEditingName(event.target.value)}
                    value={editingName}
                  />
                  <Button
                    data-ui-action="update-application"
                    disabled={pending !== ""}
                    size="sm"
                    type="submit"
                  >
                    {translate("Save")}</Button>
                </form>
              ) : (
                <RowActions>
                  <Button
                    aria-label={"Edit application " + application.displayName}
                    onClick={() => {
                      setEditingId(application.id);
                      setEditingName(application.displayName);
                    }}
                    size="sm"
                    type="button"
                    variant="ghost"
                  >
                    <Edit3 aria-hidden="true" className="size-3.5" />
                    {translate("Edit")}</Button>
                  {application.status === activeStatus ? (
                    <Button
                      className="text-rose-300 hover:text-rose-200"
                      data-ui-action="archive-application"
                      disabled={pending !== ""}
                      onClick={() => {
                        if (!window.confirm(translate(`Archive application ${application.displayName}?`))) return;
                        void runMutation(
                          "application-archive-" + application.id,
                          () => archiveApplication(csrfToken, application),
                          [reload],
                          "Application archived.",
                        );
                      }}
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <Archive aria-hidden="true" className="size-3.5" />
                      {translate("Archive")}</Button>
                  ) : (
                    <Button
                      data-ui-action="restore-application"
                      disabled={pending !== ""}
                      onClick={() =>
                        void runMutation(
                          "application-restore-" + application.id,
                          () => restoreApplication(csrfToken, application),
                          [reload],
                          "Application restored.",
                        )
                      }
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <RotateCcw aria-hidden="true" className="size-3.5" />
                      {translate("Restore")}</Button>
                  )}
                </RowActions>
              )}
            </ResourceRow>
          ))}
        </ResourceListState>
        <Pagination cursor={cursor} nextPageToken={page?.nextPageToken} />
        <CreateFormShell onSubmit={submitCreate} title={translate("Create application")}>
          <FormField label={translate("Slug")} name="application-slug">
            <input
              className={inputClassName}
              disabled={tenant.status !== activeStatus}
              id="application-slug"
              name="application-slug"
              onChange={(event) => setSlug(event.target.value)}
              placeholder={translate("desktop-client")}
              value={slug}
            />
          </FormField>
          <FormField label={translate("Display name")} name="application-display-name">
            <input
              className={inputClassName}
              disabled={tenant.status !== activeStatus}
              id="application-display-name"
              name="application-display-name"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder={translate("Desktop Client")}
              value={displayName}
            />
          </FormField>
          <Button
            className="w-full"
            data-ui-action="create-application"
            disabled={pending !== "" || tenant.status !== activeStatus}
            type="submit"
          >
            <PendingIcon pending={pending === "application-create"} />
            {translate("Create application")}</Button>
        </CreateFormShell>
      </CardContent>
    </Card>
  );
}

function EnvironmentPanel({
  application,
  csrfToken,
  cursor,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  page,
  pending,
  query,
  reload,
  runMutation,
  tenant,
}: {
  application?: ApplicationRecord;
  csrfToken: string;
  cursor: Cursor;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  page?: { environments: EnvironmentRecord[]; nextPageToken?: string | null };
  pending: string;
  query: string;
  reload: () => Promise<unknown>;
  runMutation: MutationRunner;
  tenant?: TenantRecord;
}) {
  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [environmentType, setEnvironmentType] = useState<EnvironmentType>(
    "ENVIRONMENT_TYPE_DEVELOPMENT",
  );
  const [isProtected, setIsProtected] = useState(false);
  const [editingId, setEditingId] = useState("");
  const [editingName, setEditingName] = useState("");
  const [editingType, setEditingType] = useState<EnvironmentType>(
    "ENVIRONMENT_TYPE_DEVELOPMENT",
  );
  const [editingProtected, setEditingProtected] = useState(false);

  if (!tenant || !application) {
    return (
      <SelectionPlaceholder
        description={translate("Choose an application to configure its runtime environments.")}
        icon={CloudCog}
        title={translate("Environments")}
      />
    );
  }
  const selectedTenant = tenant;
  const selectedApplication = application;

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    let created: EnvironmentRecord | undefined;
    const ok = await runMutation(
      "environment-create",
      async () => {
        created = await createEnvironment(
          csrfToken,
          selectedTenant.id,
          selectedApplication.id,
          {
            displayName,
            environmentType,
            isProtected,
            slug,
          },
        );
      },
      [reload],
      "Environment created.",
    );
    if (ok && created) {
      setSlug("");
      setDisplayName("");
      setEnvironmentType("ENVIRONMENT_TYPE_DEVELOPMENT");
      setIsProtected(false);
      onQueryChange(created.slug);
    }
  }

  async function submitEdit(event: FormEvent, environment: EnvironmentRecord) {
    event.preventDefault();
    const ok = await runMutation(
      "environment-update-" + environment.id,
      () =>
        updateEnvironment(csrfToken, environment, {
          displayName: editingName,
          environmentType: editingType,
          isProtected: editingProtected,
        }),
      [reload],
      "Environment updated.",
    );
    if (ok) setEditingId("");
  }

  const canCreate =
    tenant.status === activeStatus && application.status === activeStatus;

  return (
    <Card className="min-w-0" data-ui-action="list-environments">
      <PanelHeader
        description={translate(`Runtime boundaries for ${application.displayName}.`)}
        icon={CloudCog}
        title={translate("Environments")}
      />
      <CardContent className="space-y-4">
        <FilterBar
          checked={includeArchived}
          checkedLabel="Archived"
          checkboxName="include-archived-environments"
          onCheckedChange={onIncludeArchivedChange}
          onQueryChange={onQueryChange}
          placeholder={translate("Search environments…")}
          query={query}
        />
        <ResourceListState
          emptyMessage={translate("No environments match this view.")}
          isLoading={!page}
          items={page?.environments}
        >
          {(page?.environments ?? []).map((environment) => (
            <ResourceRow
              key={environment.id}
              slug={environment.slug}
              status={environment.status}
              testId={"environment-" + environment.slug}
              title={environment.displayName}
            >
              <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-[0.12em] text-slate-500">
                <span>{environmentLabel(environment.environmentType)}</span>
                {environment.isProtected && (
                  <span className="inline-flex items-center gap-1 text-amber-300">
                    <LockKeyhole aria-hidden="true" className="size-3" />
                    {translate("Protected")}</span>
                )}
              </div>
              {editingId === environment.id ? (
                <form
                  className="mt-3 space-y-2 rounded-lg border border-white/8 bg-black/15 p-3"
                  onSubmit={(event) => submitEdit(event, environment)}
                >
                  <FormField
                    label={translate("Display name")}
                    name={"environment-name-" + environment.id}
                  >
                    <input
                      className={inputClassName}
                      id={"environment-name-" + environment.id}
                      onChange={(event) => setEditingName(event.target.value)}
                      value={editingName}
                    />
                  </FormField>
                  <FormField
                    label={translate("Type")}
                    name={"environment-type-" + environment.id}
                  >
                    <EnvironmentTypeSelect
                      id={"environment-type-" + environment.id}
                      onChange={setEditingType}
                      value={editingType}
                    />
                  </FormField>
                  <Checkbox
                    checked={editingProtected}
                    label={translate("Protect from archival")}
                    name={"environment-protected-" + environment.id}
                    onChange={setEditingProtected}
                  />
                  <div className="flex gap-2">
                    <Button
                      data-ui-action="update-environment"
                      disabled={pending !== ""}
                      size="sm"
                      type="submit"
                    >
                      {translate("Save changes")}</Button>
                    <Button
                      onClick={() => setEditingId("")}
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      {translate("Cancel")}</Button>
                  </div>
                </form>
              ) : (
                <RowActions>
                  <Button
                    aria-label={"Edit environment " + environment.displayName}
                    onClick={() => {
                      setEditingId(environment.id);
                      setEditingName(environment.displayName);
                      setEditingType(environment.environmentType);
                      setEditingProtected(environment.isProtected);
                    }}
                    size="sm"
                    type="button"
                    variant="ghost"
                  >
                    <Edit3 aria-hidden="true" className="size-3.5" />
                    {translate("Edit")}</Button>
                  {environment.status === activeStatus ? (
                    <Button
                      className="text-rose-300 hover:text-rose-200"
                      data-ui-action="archive-environment"
                      disabled={pending !== "" || environment.isProtected}
                      onClick={() => {
                        if (!window.confirm(translate(`Archive environment ${environment.displayName}?`))) return;
                        void runMutation(
                          "environment-archive-" + environment.id,
                          () => archiveEnvironment(csrfToken, environment),
                          [reload],
                          "Environment archived.",
                        );
                      }}
                      size="sm"
                      title={
                        translate(environment.isProtected
                          ? "Remove protection before archiving."
                          : "Archive environment")
                      }
                      type="button"
                      variant="ghost"
                    >
                      <Archive aria-hidden="true" className="size-3.5" />
                      {translate("Archive")}</Button>
                  ) : (
                    <Button
                      data-ui-action="restore-environment"
                      disabled={pending !== ""}
                      onClick={() =>
                        void runMutation(
                          "environment-restore-" + environment.id,
                          () => restoreEnvironment(csrfToken, environment),
                          [reload],
                          "Environment restored.",
                        )
                      }
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <RotateCcw aria-hidden="true" className="size-3.5" />
                      {translate("Restore")}</Button>
                  )}
                </RowActions>
              )}
            </ResourceRow>
          ))}
        </ResourceListState>
        <Pagination cursor={cursor} nextPageToken={page?.nextPageToken} />
        <CreateFormShell onSubmit={submitCreate} title={translate("Create environment")}>
          <FormField label={translate("Slug")} name="environment-slug">
            <input
              className={inputClassName}
              disabled={!canCreate}
              id="environment-slug"
              name="environment-slug"
              onChange={(event) => setSlug(event.target.value)}
              placeholder={translate("production")}
              value={slug}
            />
          </FormField>
          <FormField label={translate("Display name")} name="environment-display-name">
            <input
              className={inputClassName}
              disabled={!canCreate}
              id="environment-display-name"
              name="environment-display-name"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder={translate("Production")}
              value={displayName}
            />
          </FormField>
          <FormField label={translate("Type")} name="environment-type">
            <EnvironmentTypeSelect
              disabled={!canCreate}
              id="environment-type"
              onChange={setEnvironmentType}
              value={environmentType}
            />
          </FormField>
          <Checkbox
            checked={isProtected}
            disabled={!canCreate}
            label={translate("Protect from archival")}
            name="environment-protected"
            onChange={setIsProtected}
          />
          <Button
            className="w-full"
            data-ui-action="create-environment"
            disabled={pending !== "" || !canCreate}
            type="submit"
          >
            <PendingIcon pending={pending === "environment-create"} />
            {translate("Create environment")}</Button>
        </CreateFormShell>
      </CardContent>
    </Card>
  );
}

function MembershipPanel({
  csrfToken,
  cursor,
  includeRemoved,
  onIncludeRemovedChange,
  page,
  pending,
  reload,
  runMutation,
  tenant,
}: {
  csrfToken: string;
  cursor: Cursor;
  includeRemoved: boolean;
  onIncludeRemovedChange: (value: boolean) => void;
  page?: { memberships: TenantMembershipRecord[]; nextPageToken?: string | null };
  pending: string;
  reload: () => Promise<unknown>;
  runMutation: MutationRunner;
  tenant?: TenantRecord;
}) {
  const [actorId, setActorId] = useState("");

  if (!tenant) {
    return (
      <SelectionPlaceholder
        description={translate("Choose a tenant to review and manage its direct memberships.")}
        icon={Users}
        title={translate("Tenant memberships")}
      />
    );
  }
  const selectedTenant = tenant;

  async function submitMembership(event: FormEvent) {
    event.preventDefault();
    const existing = page?.memberships.find(
      (membership) => membership.actorId.toLowerCase() === actorId.toLowerCase(),
    );
    const ok = await runMutation(
      "membership-set-" + actorId,
      () =>
        setTenantMembership(
          csrfToken,
          selectedTenant.id,
          actorId,
          existing?.version ?? 0,
        ),
      [reload],
      existing ? "Membership reactivated." : "Membership added.",
    );
    if (ok) setActorId("");
  }

  return (
    <Card data-ui-action="list-tenant-memberships">
      <CardHeader className="gap-4 border-b border-white/8 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-start gap-3">
          <div className="grid size-10 shrink-0 place-items-center rounded-xl bg-violet-400/10 text-violet-300">
            <Users aria-hidden="true" className="size-4" />
          </div>
          <div>
            <CardTitle>{translate("Tenant memberships")}</CardTitle>
            <CardDescription>
              {translate("Direct actors allowed to operate within")} {tenant.displayName}.
            </CardDescription>
          </div>
        </div>
        <Checkbox
          checked={includeRemoved}
          label={translate("Show removed memberships")}
          name="include-removed-memberships"
          onChange={onIncludeRemovedChange}
        />
      </CardHeader>
      <CardContent className="grid gap-5 pt-5 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div>
          {page?.memberships.length ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {page.memberships.map((membership) => (
                <div
                  className="rounded-xl border border-white/8 bg-white/[0.025] p-4"
                  data-testid={"membership-" + membership.actorId}
                  key={membership.actorId}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="truncate font-mono text-xs text-slate-300">
                        {membership.actorId}
                      </p>
                      <p className="mt-1 text-[10px] uppercase tracking-[0.12em] text-slate-600">
                        {translate("Version")} {membership.version}
                      </p>
                    </div>
                    <Badge
                      variant={
                        membership.status === activeMembership
                          ? "success"
                          : "planned"
                      }
                    >
                      {translate(membership.status === activeMembership ? "Active" : "Removed")}
                    </Badge>
                  </div>
                  <div className="mt-4">
                    {membership.status === activeMembership ? (
                      <Button
                        className="text-rose-300 hover:text-rose-200"
                        data-ui-action="remove-tenant-membership"
                        disabled={pending !== ""}
                        onClick={() => {
                          if (!window.confirm(translate(`Remove membership ${membership.actorId}?`))) return;
                          void runMutation(
                            "membership-remove-" + membership.actorId,
                            () => removeTenantMembership(csrfToken, membership),
                            [reload],
                            "Membership removed.",
                          );
                        }}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <Trash2 aria-hidden="true" className="size-3.5" />
                        {translate("Remove")}</Button>
                    ) : (
                      <Button
                        data-ui-action="set-tenant-membership"
                        disabled={pending !== "" || tenant.status !== activeStatus}
                        onClick={() =>
                          void runMutation(
                            "membership-restore-" + membership.actorId,
                            () =>
                              setTenantMembership(
                                csrfToken,
                                tenant.id,
                                membership.actorId,
                                membership.version,
                              ),
                            [reload],
                            "Membership reactivated.",
                          )
                        }
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <RotateCcw aria-hidden="true" className="size-3.5" />
                        {translate("Reactivate")}</Button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <EmptyMessage>{translate("No memberships match this view.")}</EmptyMessage>
          )}
          <Pagination cursor={cursor} nextPageToken={page?.nextPageToken} />
        </div>
        <CreateFormShell onSubmit={submitMembership} title={translate("Add membership")}>
          <FormField label={translate("Actor ID")} name="membership-actor-id">
            <input
              className={inputClassName}
              disabled={tenant.status !== activeStatus}
              id="membership-actor-id"
              name="membership-actor-id"
              onChange={(event) => setActorId(event.target.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
              value={actorId}
            />
          </FormField>
          <p className="text-xs leading-5 text-slate-500">
            {translate("Use the immutable Passport subject identifier. Removed members are reactivated without losing history.")}</p>
          <Button
            className="w-full"
            data-ui-action="set-tenant-membership"
            disabled={pending !== "" || tenant.status !== activeStatus}
            type="submit"
          >
            <UserPlus aria-hidden="true" className="size-4" />
            {translate("Add or reactivate")}</Button>
        </CreateFormShell>
      </CardContent>
    </Card>
  );
}

function PanelHeader({
  description,
  icon: Icon,
  title,
}: {
  description: string;
  icon: typeof Building2;
  title: string;
}) {
  return (
    <CardHeader className="flex-row items-start gap-3 border-b border-white/8">
      <div className="grid size-9 shrink-0 place-items-center rounded-lg bg-sky-400/10 text-sky-300">
        <Icon aria-hidden="true" className="size-4" />
      </div>
      <div>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </div>
    </CardHeader>
  );
}

function FilterBar({
  checked,
  checkedLabel,
  checkboxName,
  onCheckedChange,
  onQueryChange,
  placeholder,
  query,
}: {
  checked: boolean;
  checkedLabel: string;
  checkboxName: string;
  onCheckedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  placeholder: string;
  query: string;
}) {
  return (
    <div className="space-y-2">
      <div className="relative">
        <Search
          aria-hidden="true"
          className="pointer-events-none absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-slate-600"
        />
        <input
          aria-label={placeholder.replace("…", "")}
          className={cn(inputClassName, "pl-9")}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder={placeholder}
          type="search"
          value={query}
        />
      </div>
      <Checkbox
        checked={checked}
        label={"Include " + checkedLabel.toLowerCase()}
        name={checkboxName}
        onChange={onCheckedChange}
      />
    </div>
  );
}

function ResourceRow({
  active = false,
  children,
  onSelect,
  slug,
  status,
  testId,
  title,
}: {
  active?: boolean;
  children?: ReactNode;
  onSelect?: () => void;
  slug: string;
  status: string;
  testId: string;
  title: string;
}) {
  return (
    <article
      className={cn(
        "rounded-xl border p-3 transition-colors",
        active
          ? "border-sky-400/35 bg-sky-400/[0.08]"
          : "border-white/8 bg-white/[0.02]",
      )}
      data-testid={testId}
    >
      <div className="flex min-w-0 items-start justify-between gap-3">
        <button
          className={cn(
            "min-w-0 text-left outline-none",
            onSelect && "cursor-pointer focus-visible:text-sky-300",
          )}
          disabled={!onSelect}
          onClick={onSelect}
          type="button"
        >
          <span className="block truncate text-sm font-medium text-slate-200">
            {title}
          </span>
          <span className="mt-0.5 block truncate font-mono text-[10px] text-slate-600">
            {slug}
          </span>
        </button>
        <Badge variant={status === activeStatus ? "success" : "planned"}>
          {translate(status === activeStatus ? "Active" : "Archived")}
        </Badge>
      </div>
      {children}
    </article>
  );
}

function ResourceListState({
  children,
  emptyMessage,
  error,
  isLoading,
  items,
}: {
  children: ReactNode;
  emptyMessage: string;
  error?: unknown;
  isLoading: boolean;
  items?: unknown[];
}) {
  if (isLoading && !items) {
    return (
      <div className="grid h-28 place-items-center text-slate-500">
        <LoaderCircle aria-hidden="true" className="size-5 animate-spin" />
      </div>
    );
  }
  if (error) {
    return (
      <div className="flex items-start gap-2 rounded-xl border border-rose-400/15 bg-rose-400/5 p-3 text-xs leading-5 text-rose-200">
        <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
        {translate(platformErrorMessage(error))}
      </div>
    );
  }
  if (!items?.length) return <EmptyMessage>{emptyMessage}</EmptyMessage>;
  return <div className="max-h-[28rem] space-y-2 overflow-y-auto pr-1">{children}</div>;
}

function EmptyMessage({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-white/10 px-4 py-8 text-center text-xs text-slate-600">
      {children}
    </div>
  );
}

function SelectionPlaceholder({
  description,
  icon: Icon,
  title,
}: {
  description: string;
  icon: typeof Boxes;
  title: string;
}) {
  return (
    <Card className="grid min-h-72 place-items-center p-8 text-center">
      <div>
        <div className="mx-auto grid size-12 place-items-center rounded-2xl bg-white/[0.04] text-slate-600">
          <Icon aria-hidden="true" className="size-5" />
        </div>
        <h2 className="mt-4 text-sm font-semibold text-slate-300">{title}</h2>
        <p className="mx-auto mt-2 max-w-64 text-xs leading-5 text-slate-600">
          {description}
        </p>
      </div>
    </Card>
  );
}

function CreateFormShell({
  children,
  onSubmit,
  title,
}: {
  children: ReactNode;
  onSubmit: (event: FormEvent) => void;
  title: string;
}) {
  return (
    <form
      className="space-y-3 rounded-xl border border-white/8 bg-white/[0.025] p-4"
      onSubmit={onSubmit}
    >
      <div className="flex items-center gap-2 text-xs font-semibold text-slate-300">
        <Plus aria-hidden="true" className="size-3.5 text-sky-400" />
        {title}
      </div>
      {children}
    </form>
  );
}

function FormField({
  children,
  label,
  name,
}: {
  children: ReactNode;
  label: string;
  name: string;
}) {
  return (
    <div>
      <label className={labelClassName} htmlFor={name}>
        {label}
      </label>
      {children}
    </div>
  );
}

function Checkbox({
  checked,
  disabled = false,
  label,
  name,
  onChange,
}: {
  checked: boolean;
  disabled?: boolean;
  label: string;
  name: string;
  onChange: (value: boolean) => void;
}) {
  return (
    <label
      className={cn(
        "inline-flex cursor-pointer items-center gap-2 text-xs text-slate-500",
        disabled && "cursor-not-allowed opacity-50",
      )}
      htmlFor={name}
    >
      <input
        checked={checked}
        className="size-3.5 accent-sky-400"
        disabled={disabled}
        id={name}
        onChange={(event) => onChange(event.target.checked)}
        type="checkbox"
      />
      {label}
    </label>
  );
}

function EnvironmentTypeSelect({
  disabled = false,
  id,
  onChange,
  value,
}: {
  disabled?: boolean;
  id: string;
  onChange: (value: EnvironmentType) => void;
  value: EnvironmentType;
}) {
  return (
    <select
      className={inputClassName}
      disabled={disabled}
      id={id}
      onChange={(event) => onChange(event.target.value as EnvironmentType)}
      value={value}
    >
      <option value="ENVIRONMENT_TYPE_DEVELOPMENT">{translate("Development")}</option>
      <option value="ENVIRONMENT_TYPE_STAGING">{translate("Staging")}</option>
      <option value="ENVIRONMENT_TYPE_PRODUCTION">{translate("Production")}</option>
    </select>
  );
}

function RowActions({ children }: { children: ReactNode }) {
  return <div className="mt-2 flex flex-wrap gap-1">{children}</div>;
}

function Pagination({
  cursor,
  nextPageToken,
}: {
  cursor: Cursor;
  nextPageToken?: string | null;
}) {
  if (!cursor.hasPrevious && !nextPageToken) return null;
  return (
    <div className="flex items-center justify-end gap-2 pt-1">
      <Button
        aria-label={translate("Previous page")}
        disabled={!cursor.hasPrevious}
        onClick={cursor.previous}
        size="sm"
        type="button"
        variant="ghost"
      >
        <ChevronLeft aria-hidden="true" className="size-3.5" />
        {translate("Previous")}</Button>
      <Button
        aria-label={translate("Next page")}
        disabled={!nextPageToken}
        onClick={() => nextPageToken && cursor.next(nextPageToken)}
        size="sm"
        type="button"
        variant="ghost"
      >
        {translate("Next")}<ChevronRight aria-hidden="true" className="size-3.5" />
      </Button>
    </div>
  );
}

function PendingIcon({ pending }: { pending: boolean }) {
  return pending ? (
    <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
  ) : (
    <Plus aria-hidden="true" className="size-4" />
  );
}

function ScopeCrumb({
  active,
  children,
}: {
  active: boolean;
  children: ReactNode;
}) {
  return (
    <span className={cn("max-w-28 truncate", active && "text-sky-300")}>
      {children}
    </span>
  );
}

function environmentLabel(type: EnvironmentType) {
  switch (type) {
    case "ENVIRONMENT_TYPE_PRODUCTION":
      return translate("Production");
    case "ENVIRONMENT_TYPE_STAGING":
      return translate("Staging");
    default:
      return translate("Development");
  }
}

function usePageCursor() {
  const [tokens, setTokens] = useState([""]);
  return {
    current: tokens.at(-1) ?? "",
    hasPrevious: tokens.length > 1,
    next: (token: string) => setTokens((current) => [...current, token]),
    previous: () =>
      setTokens((current) =>
        current.length > 1 ? current.slice(0, -1) : current,
      ),
    reset: () => setTokens([""]),
  };
}
