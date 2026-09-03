"use client";

import {
  Archive,
  BookKey,
  CheckCircle2,
  CircleAlert,
  FileClock,
  KeyRound,
  LoaderCircle,
  Pencil,
  Play,
  Plus,
  RotateCcw,
  Search,
  ShieldCheck,
  Trash2,
  UserRoundCog,
  ListChecks,
} from "lucide-react";
import { type FormEvent, type ReactNode, useState } from "react";
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
  archivePolicyRule,
  archivePermission,
  archiveRole,
  authorizationErrorMessage,
  checkCurrentActorPermission,
  createPolicyRule,
  createPermission,
  createRole,
  isAuthorizationVersionConflict,
  listAllPermissions,
  listPolicyRevisions,
  listPolicyRules,
  listRoleBindings,
  listRoles,
  removeRoleBinding,
  restorePolicyRule,
  restorePermission,
  restoreRole,
  setRoleBinding,
  simulateAuthorization,
  updatePolicyRule,
  updatePermission,
  updateRole,
  type AuthorizationDecisionRecord,
  type AuthorizationRoleRecord,
  type AuthorizationScopeInput,
  type PermissionRecord,
  type PolicyEffect,
  type PolicyRuleRecord,
  type PolicySubjectType,
  type RoleBindingRecord,
} from "@/lib/api/authorization-management";
import {
  createRuleDraft,
  RuleEditor,
  toRuleInput,
  type TargetingRuleDraft,
} from "@/features/targeting/rule-editor";
import type { TargetingValueInput } from "@/lib/api/targeting-management";
import {
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import { cn } from "@/lib/utils/cn";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const textAreaClassName = cn(inputClassName, "h-20 resize-y py-2");
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const activeStatus = "AUTHORIZATION_RESOURCE_STATUS_ACTIVE";
const allowEffect = "POLICY_EFFECT_ALLOW";
const denyEffect = "POLICY_EFFECT_DENY";
const actorSubject = "POLICY_SUBJECT_TYPE_ACTOR";
const roleSubject = "POLICY_SUBJECT_TYPE_ROLE";
const anySubject = "POLICY_SUBJECT_TYPE_ANY";

type WorkspaceTab = "permissions" | "roles" | "bindings" | "policies" | "simulator";
type ScopeFieldsValue = {
  applicationId: string;
  environmentId: string;
  tenantId: string;
};
type MutationRunner = (
  key: string,
  work: () => Promise<unknown>,
  refresh: Array<() => Promise<unknown>>,
  successMessage: string,
) => Promise<boolean>;

const emptyScope: ScopeFieldsValue = {
  applicationId: "",
  environmentId: "",
  tenantId: "",
};
const platformPage = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function AuthorizationWorkspace({
  actorId,
  csrfToken,
}: {
  actorId: string;
  csrfToken: string;
}) {
  const [activeTab, setActiveTab] = useState<WorkspaceTab>("roles");
  const hydrated = useHydrated();
  const [pending, setPending] = useState("");
  const [permissionQuery, setPermissionQuery] = useState("");
  const [includeArchivedPermissions, setIncludeArchivedPermissions] = useState(false);
  const [scopeTenantId, setScopeTenantId] = useState("");
  const [scopeApplicationId, setScopeApplicationId] = useState("");
  const [roleQuery, setRoleQuery] = useState("");
  const [includeArchivedRoles, setIncludeArchivedRoles] = useState(false);
  const [bindingActorQuery, setBindingActorQuery] = useState("");
  const [includeArchivedBindings, setIncludeArchivedBindings] = useState(false);
  const [policyQuery, setPolicyQuery] = useState("");
  const [includeArchivedPolicies, setIncludeArchivedPolicies] = useState(false);
  const [revisionResourceType, setRevisionResourceType] = useState("");
  const [revisionResourceId, setRevisionResourceId] = useState("");

  const scopeTenants = useSWR(hydrated ? "authorization-scope-tenants" : null, () =>
    listTenants(platformPage),
  );
  const scopeApplications = useSWR(
    scopeTenantId ? ["authorization-scope-applications", scopeTenantId] : null,
    () => listApplications(scopeTenantId, platformPage),
  );

  const systemPermissions = useSWR(
    ["authorization-system-permissions", permissionQuery],
    () => listAllPermissions({ query: permissionQuery }),
    { keepPreviousData: true },
  );
  const hasApplicationScope = isUuid(scopeTenantId) && isUuid(scopeApplicationId);
  const applicationPermissions = useSWR(
    hasApplicationScope
      ? [
          "authorization-application-permissions",
          scopeTenantId,
          scopeApplicationId,
          permissionQuery,
          includeArchivedPermissions,
        ]
      : null,
    () =>
      listAllPermissions({
        applicationId: scopeApplicationId,
        includeArchived: includeArchivedPermissions,
        query: permissionQuery,
        tenantId: scopeTenantId,
      }),
    { keepPreviousData: true },
  );
  const permissions = [
    ...(applicationPermissions.data?.permissions ?? []),
    ...(systemPermissions.data?.permissions ?? []),
  ];
  const roles = useSWR(
    [
      "authorization-roles",
      roleQuery,
      includeArchivedRoles,
      hasApplicationScope ? scopeTenantId : "",
      hasApplicationScope ? scopeApplicationId : "",
    ],
    () =>
      listRoles({
        includeArchived: includeArchivedRoles,
        pageSize: 100,
        query: roleQuery,
        tenantId: hasApplicationScope ? scopeTenantId : "",
        applicationId: hasApplicationScope ? scopeApplicationId : "",
      }),
    { keepPreviousData: true },
  );
  const bindings = useSWR(
    [
      "authorization-bindings",
      bindingActorQuery,
      scopeTenantId,
      scopeApplicationId,
      includeArchivedBindings,
    ],
    () =>
      listRoleBindings({
        actorId: bindingActorQuery,
        includeArchived: includeArchivedBindings,
        pageSize: 100,
        tenantId: hasApplicationScope ? scopeTenantId : "",
        applicationId: hasApplicationScope ? scopeApplicationId : "",
      }),
    { keepPreviousData: true },
  );
  const policies = useSWR(
    [
      "authorization-policies",
      policyQuery,
      scopeTenantId,
      scopeApplicationId,
      includeArchivedPolicies,
    ],
    () =>
      listPolicyRules({
        includeArchived: includeArchivedPolicies,
        pageSize: 100,
        query: policyQuery,
        tenantId: hasApplicationScope ? scopeTenantId : "",
        applicationId: hasApplicationScope ? scopeApplicationId : "",
      }),
    { keepPreviousData: true },
  );
  const revisions = useSWR(
    ["authorization-revisions", revisionResourceType, revisionResourceId],
    () =>
      listPolicyRevisions({
        pageSize: 100,
        resourceId: revisionResourceId,
        resourceType: revisionResourceType,
      }),
    { keepPreviousData: true },
  );

  const runMutation: MutationRunner = async (key, work, refresh, message) => {
    setPending(key);
    try {
      await work();
      await Promise.all(refresh.map((reload) => reload()));
      toast.success(translate(message));
      return true;
    } catch (error) {
      await Promise.allSettled(refresh.map((reload) => reload()));
      toast.error(
        translate(isAuthorizationVersionConflict(error)
          ? "This policy resource changed elsewhere. Latest data loaded; review it and retry."
          : authorizationErrorMessage(error)),
      );
      return false;
    } finally {
      setPending("");
    }
  };

  const tabs: Array<{ id: WorkspaceTab; label: string; icon: typeof ShieldCheck }> = [
    { id: "permissions", label: "Application permissions", icon: ListChecks },
    { id: "roles", label: "Roles", icon: BookKey },
    { id: "bindings", label: "Role bindings", icon: UserRoundCog },
    { id: "policies", label: "Policy rules", icon: ShieldCheck },
    { id: "simulator", label: "Simulator & revisions", icon: Play },
  ];

  return (
    <div
      aria-busy={!hydrated}
      className={cn("space-y-6", !hydrated && "pointer-events-none")}
      data-authorization-workspace
      data-hydrated={hydrated}
    >
      <section className="theme-hero-violet overflow-hidden rounded-3xl border border-violet-300/10 bg-[linear-gradient(135deg,rgba(139,92,246,0.14),rgba(15,23,42,0.84)_55%,rgba(2,6,23,0.96))] p-6 sm:p-8">
        <Badge className="border-violet-400/20 bg-violet-400/10 text-violet-300">
          <KeyRound aria-hidden="true" className="size-3" />
          {translate("Casbin policy plane")}</Badge>
        <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
          {translate("Authorization control center")}</h1>
        <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
          {translate("Compose immutable platform permissions into roles, bind them to actors at an exact scope, add explicit policy exceptions, and inspect every revision before testing the resulting decision.")}</p>
      </section>

      <Card>
        <CardHeader>
          <CardTitle>{translate("Application authorization scope")}</CardTitle>
          <CardDescription>
            {translate("Select one tenant and application to manage its permissions, roles, ACLs, and ABAC policies.")}
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 sm:grid-cols-2">
          <SearchableSelect
            ariaLabel={translate("Tenant")}
            className={inputClassName}
            emptyLabel={translate("Choose a tenant")}
            label={translate("Tenant")}
            labelClassName={labelClassName}
            name="authorizationTenantId"
            onChange={(tenantId) => {
              setScopeTenantId(tenantId);
              setScopeApplicationId("");
            }}
            options={(scopeTenants.data?.tenants ?? []).map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))}
            value={scopeTenantId}
          />
          <SearchableSelect
            ariaLabel={translate("Application")}
            className={inputClassName}
            disabled={!scopeTenantId}
            emptyLabel={translate("Choose an application")}
            label={translate("Application")}
            labelClassName={labelClassName}
            name="authorizationApplicationId"
            onChange={setScopeApplicationId}
            options={(scopeApplications.data?.applications ?? []).map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))}
            value={scopeApplicationId}
          />
          {(scopeTenants.error ?? scopeApplications.error) && (
            <div className="sm:col-span-2">
              <ResourceError error={scopeTenants.error ?? scopeApplications.error} />
            </div>
          )}
        </CardContent>
      </Card>

      <nav
        aria-label={translate("Authorization workspace")}
        className="grid gap-2 rounded-2xl border border-white/8 bg-white/[0.025] p-2 sm:grid-cols-2 xl:grid-cols-5"
      >
        {tabs.map((tab) => {
          const Icon = tab.icon;
          return (
            <button
              className={cn(
                "flex h-11 items-center justify-center gap-2 rounded-xl px-3 text-sm font-medium transition",
                activeTab === tab.id
                  ? "bg-violet-400/15 text-violet-200 ring-1 ring-violet-300/20"
                  : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
              )}
              data-testid={`authorization-tab-${tab.id}`}
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              type="button"
            >
              <Icon aria-hidden="true" className="size-4" />
              {translate(tab.label)}
            </button>
          );
        })}
      </nav>

      {activeTab === "permissions" && (
        <PermissionsPanel
          applicationId={scopeApplicationId}
          csrfToken={csrfToken}
          error={applicationPermissions.error}
          includeArchived={includeArchivedPermissions}
          onIncludeArchivedChange={setIncludeArchivedPermissions}
          onQueryChange={setPermissionQuery}
          pending={pending}
          permissions={applicationPermissions.data?.permissions ?? []}
          query={permissionQuery}
          reloadPermissions={applicationPermissions.mutate}
          reloadRevisions={revisions.mutate}
          runMutation={runMutation}
          tenantId={scopeTenantId}
        />
      )}

      {activeTab === "roles" && (
        <RolesPanel
          csrfToken={csrfToken}
          includeArchived={includeArchivedRoles}
          onIncludeArchivedChange={setIncludeArchivedRoles}
          onPermissionQueryChange={setPermissionQuery}
          onRoleQueryChange={setRoleQuery}
          pending={pending}
          permissionQuery={permissionQuery}
          applicationId={scopeApplicationId}
          permissions={permissions}
          permissionsError={systemPermissions.error ?? applicationPermissions.error}
          reloadRevisions={revisions.mutate}
          reloadRoles={roles.mutate}
          roleQuery={roleQuery}
          roles={roles.data?.roles ?? []}
          rolesError={roles.error}
          runMutation={runMutation}
          tenantId={scopeTenantId}
        />
      )}
      {activeTab === "bindings" && (
        <BindingsPanel
          actorQuery={bindingActorQuery}
          bindings={bindings.data?.roleBindings ?? []}
          csrfToken={csrfToken}
          error={bindings.error}
          includeArchived={includeArchivedBindings}
          onActorQueryChange={setBindingActorQuery}
          onIncludeArchivedChange={setIncludeArchivedBindings}
          pending={pending}
          reloadBindings={bindings.mutate}
          reloadRevisions={revisions.mutate}
          roles={roles.data?.roles ?? []}
          runMutation={runMutation}
          applicationId={scopeApplicationId}
          tenantId={scopeTenantId}
        />
      )}
      {activeTab === "policies" && (
        <PoliciesPanel
          csrfToken={csrfToken}
          error={policies.error}
          includeArchived={includeArchivedPolicies}
          onIncludeArchivedChange={setIncludeArchivedPolicies}
          onQueryChange={setPolicyQuery}
          pending={pending}
          permissions={permissions}
          policies={policies.data?.policyRules ?? []}
          query={policyQuery}
          reloadPolicies={policies.mutate}
          reloadRevisions={revisions.mutate}
          runMutation={runMutation}
          applicationId={scopeApplicationId}
          tenantId={scopeTenantId}
        />
      )}
      {activeTab === "simulator" && (
        <SimulatorPanel
          actorId={actorId}
          applicationId={scopeApplicationId}
          csrfToken={csrfToken}
          onRevisionResourceIdChange={setRevisionResourceId}
          onRevisionResourceTypeChange={setRevisionResourceType}
          pending={pending}
          permissions={permissions}
          revisionResourceId={revisionResourceId}
          revisionResourceType={revisionResourceType}
          revisions={revisions.data?.revisions ?? []}
          revisionsError={revisions.error}
          runMutation={runMutation}
          tenantId={scopeTenantId}
        />
      )}
    </div>
  );
}

function PermissionsPanel({
  applicationId,
  csrfToken,
  error,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  pending,
  permissions,
  query,
  reloadPermissions,
  reloadRevisions,
  runMutation,
  tenantId,
}: {
  applicationId: string;
  csrfToken: string;
  error: unknown;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  pending: string;
  permissions: PermissionRecord[];
  query: string;
  reloadPermissions: () => Promise<unknown>;
  reloadRevisions: () => Promise<unknown>;
  runMutation: MutationRunner;
  tenantId: string;
}) {
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [editing, setEditing] = useState<PermissionRecord>();
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const validScope = isUuid(tenantId) && isUuid(applicationId);

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    const success = await runMutation(
      "create-permission",
      () =>
        createPermission(csrfToken, {
          description,
          displayName,
          key,
          scope: { applicationId, environmentId: undefined, tenantId },
        }),
      [reloadPermissions, reloadRevisions],
      "Application permission created.",
    );
    if (success) {
      setKey("");
      setDisplayName("");
      setDescription("");
    }
  }

  function beginEdit(permission: PermissionRecord) {
    setEditing(permission);
    setEditDisplayName(permission.displayName);
    setEditDescription(permission.description);
  }

  async function submitEdit(event: FormEvent) {
    event.preventDefault();
    if (!editing) return;
    const success = await runMutation(
      `update-permission-${editing.id}`,
      () =>
        updatePermission(csrfToken, editing, {
          description: editDescription,
          displayName: editDisplayName,
        }),
      [reloadPermissions, reloadRevisions],
      "Application permission updated.",
    );
    if (success) setEditing(undefined);
  }

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.75fr_1.25fr]">
      <Card data-ui-action="create-permission">
        <CardHeader>
          <CardTitle>{translate("Register application permission")}</CardTitle>
          <CardDescription>
            {translate("Permission keys are application-owned and use dotted business names such as orders.refund.")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {!validScope && (
            <p className="mb-4 rounded-lg border border-amber-400/15 bg-amber-400/[0.06] p-3 text-xs text-amber-300">
              {translate("Select a tenant and application above first.")}
            </p>
          )}
          <form className="space-y-4" onSubmit={submitCreate}>
            <Field label={translate("Permission key")}>
              <input
                className={inputClassName}
                name="applicationPermissionKey"
                onChange={(event) => setKey(event.target.value)}
                placeholder={translate("orders.refund")}
                value={key}
              />
            </Field>
            <Field label={translate("Display name")}>
              <input
                className={inputClassName}
                name="applicationPermissionDisplayName"
                onChange={(event) => setDisplayName(event.target.value)}
                value={displayName}
              />
            </Field>
            <Field label={translate("Description")}>
              <textarea
                className={textAreaClassName}
                name="applicationPermissionDescription"
                onChange={(event) => setDescription(event.target.value)}
                value={description}
              />
            </Field>
            <div className="flex justify-end">
              <Button disabled={pending !== "" || !validScope} type="submit">
                <Plus className="size-4" /> {translate("Create permission")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card data-ui-action="list-permissions">
        <CardHeader>
          <CardTitle>{translate("Application permission catalog")}</CardTitle>
          <CardDescription>
            {translate("Archiving a permission immediately makes matching RBAC, ACL, and ABAC grants inactive.")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <FilterBar
            includeArchived={includeArchived}
            onIncludeArchivedChange={onIncludeArchivedChange}
            onQueryChange={onQueryChange}
            query={query}
            queryPlaceholder={translate("Search application permissions")}
          />
          <ResourceError error={error} />
          <div className="mt-4 space-y-3">
            {permissions.map((permission) => (
              <div
                className="rounded-xl border border-white/8 bg-slate-950/45 p-4"
                data-testid={`authorization-permission-${permission.key}`}
                key={permission.id}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="break-all font-mono text-xs text-violet-300">{permission.key}</p>
                      <StatusBadge active={permission.status === activeStatus} />
                    </div>
                    <p className="mt-2 text-sm font-medium text-slate-200">{permission.displayName}</p>
                    <p className="mt-1 text-xs text-slate-500">{permission.description}</p>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button onClick={() => beginEdit(permission)} size="sm" type="button" variant="ghost">
                      <Pencil className="size-3.5" /> {translate("Edit")}
                    </Button>
                    {permission.status === activeStatus ? (
                      <Button
                        data-ui-action="archive-permission"
                        disabled={pending !== ""}
                        onClick={() => void runMutation(
                          `archive-permission-${permission.id}`,
                          () => archivePermission(csrfToken, permission),
                          [reloadPermissions, reloadRevisions],
                          "Application permission archived.",
                        )}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <Archive className="size-3.5" /> {translate("Archive")}
                      </Button>
                    ) : (
                      <Button
                        data-ui-action="restore-permission"
                        disabled={pending !== ""}
                        onClick={() => void runMutation(
                          `restore-permission-${permission.id}`,
                          () => restorePermission(csrfToken, permission),
                          [reloadPermissions, reloadRevisions],
                          "Application permission restored.",
                        )}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <RotateCcw className="size-3.5" /> {translate("Restore")}
                      </Button>
                    )}
                  </div>
                </div>
                {editing?.id === permission.id && (
                  <form className="mt-4 space-y-3 border-t border-white/8 pt-4" data-ui-action="update-permission" onSubmit={submitEdit}>
                    <Field label={translate("Display name")}>
                      <input className={inputClassName} onChange={(event) => setEditDisplayName(event.target.value)} value={editDisplayName} />
                    </Field>
                    <Field label={translate("Description")}>
                      <textarea className={textAreaClassName} onChange={(event) => setEditDescription(event.target.value)} value={editDescription} />
                    </Field>
                    <div className="flex justify-end gap-2">
                      <Button onClick={() => setEditing(undefined)} type="button" variant="ghost">{translate("Cancel")}</Button>
                      <Button disabled={pending !== ""} type="submit">{translate("Save permission")}</Button>
                    </div>
                  </form>
                )}
              </div>
            ))}
            {permissions.length === 0 && <EmptyState text={translate(validScope ? "No application permissions match this view." : "Select an application to load its permissions.")} />}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function RolesPanel({
  applicationId,
  csrfToken,
  includeArchived,
  onIncludeArchivedChange,
  onPermissionQueryChange,
  onRoleQueryChange,
  pending,
  permissionQuery,
  permissions,
  permissionsError,
  reloadRevisions,
  reloadRoles,
  roleQuery,
  roles,
  rolesError,
  runMutation,
  tenantId,
}: {
  applicationId: string;
  csrfToken: string;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onPermissionQueryChange: (value: string) => void;
  onRoleQueryChange: (value: string) => void;
  pending: string;
  permissionQuery: string;
  permissions: PermissionRecord[];
  permissionsError: unknown;
  reloadRevisions: () => Promise<unknown>;
  reloadRoles: () => Promise<unknown>;
  roleQuery: string;
  roles: AuthorizationRoleRecord[];
  rolesError: unknown;
  runMutation: MutationRunner;
  tenantId: string;
}) {
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [permissionKeys, setPermissionKeys] = useState("");
  const [editing, setEditing] = useState<AuthorizationRoleRecord>();
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editPermissions, setEditPermissions] = useState("");

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    const success = await runMutation(
      "create-role",
      () =>
        createRole(csrfToken, {
          description,
          displayName,
          key,
          permissions: parseCsv(permissionKeys),
          scope: { applicationId, environmentId: undefined, tenantId },
        }),
      [reloadRoles, reloadRevisions],
      "Role created.",
    );
    if (success) {
      setKey("");
      setDisplayName("");
      setDescription("");
      setPermissionKeys("");
    }
  }

  function beginEdit(role: AuthorizationRoleRecord) {
    setEditing(role);
    setEditDisplayName(role.displayName);
    setEditDescription(role.description);
    setEditPermissions(role.permissions.join(", "));
  }

  async function submitEdit(event: FormEvent) {
    event.preventDefault();
    if (!editing) return;
    const success = await runMutation(
      `update-role-${editing.id}`,
      () =>
        updateRole(csrfToken, editing, {
          description: editDescription,
          displayName: editDisplayName,
          permissions: parseCsv(editPermissions),
        }),
      [reloadRoles, reloadRevisions],
      "Role updated.",
    );
    if (success) setEditing(undefined);
  }

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[1.35fr_0.65fr]">
      <div className="space-y-5">
        <Card data-ui-action="list-roles">
          <CardHeader>
            <CardTitle>{translate("Roles")}</CardTitle>
            <CardDescription>
              {translate("System roles are immutable; custom roles have optimistic versions.")}</CardDescription>
          </CardHeader>
          <CardContent>
            <FilterBar
              includeArchived={includeArchived}
              onIncludeArchivedChange={onIncludeArchivedChange}
              onQueryChange={onRoleQueryChange}
              query={roleQuery}
              queryPlaceholder="Search roles"
            />
            <ResourceError error={rolesError} />
            <div className="mt-4 space-y-3">
              {roles.map((role) => (
                <div
                  className="rounded-xl border border-white/8 bg-slate-950/45 p-4"
                  data-testid={`authorization-role-${role.key}`}
                  key={role.id}
                >
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-medium text-slate-100">{role.displayName}</p>
                        <StatusBadge active={role.status === activeStatus} />
                        {role.isSystem && <Badge variant="planned">{translate("System")}</Badge>}
                      </div>
                      <p className="mt-1 font-mono text-xs text-violet-300">{role.key}</p>
                      {!role.isSystem && <ScopeSummary scope={role.scope} />}
                      <p className="mt-2 text-xs leading-5 text-slate-500">
                        {role.description || translate("No description")} {" "}{translate("· v")} {role.version}
                      </p>
                      <div className="mt-3 flex flex-wrap gap-1.5">
                        {role.permissions.map((permission) => (
                          <span
                            className="rounded-md bg-white/[0.045] px-2 py-1 font-mono text-[10px] text-slate-400"
                            key={permission}
                          >
                            {permission}
                          </span>
                        ))}
                      </div>
                    </div>
                    {!role.isSystem && (
                      <div className="flex shrink-0 gap-2">
                        <Button
                          onClick={() => beginEdit(role)}
                          size="sm"
                          type="button"
                          variant="ghost"
                        >
                          <Pencil className="size-3.5" /> {translate("Edit")}</Button>
                        {role.status === activeStatus ? (
                          <Button
                            data-ui-action="archive-role"
                            disabled={pending !== ""}
                            onClick={() => {
                              if (!window.confirm(translate(`Archive role ${role.displayName}?`))) return;
                              void runMutation(
                                `archive-role-${role.id}`,
                                () => archiveRole(csrfToken, role),
                                [reloadRoles, reloadRevisions],
                                "Role archived.",
                              );
                            }}
                            size="sm"
                            type="button"
                            variant="ghost"
                          >
                            <Archive className="size-3.5" /> {translate("Archive")}</Button>
                        ) : (
                          <Button
                            data-ui-action="restore-role"
                            disabled={pending !== ""}
                            onClick={() =>
                              void runMutation(
                                `restore-role-${role.id}`,
                                () => restoreRole(csrfToken, role),
                                [reloadRoles, reloadRevisions],
                                "Role restored.",
                              )
                            }
                            size="sm"
                            type="button"
                            variant="ghost"
                          >
                            <RotateCcw className="size-3.5" /> {translate("Restore")}</Button>
                        )}
                      </div>
                    )}
                  </div>
                  {editing?.id === role.id && (
                    <form
                      className="mt-4 grid gap-3 border-t border-white/8 pt-4"
                      data-ui-action="update-role"
                      onSubmit={submitEdit}
                    >
                      <Field label={translate("Display name")}>
                        <input
                          className={inputClassName}
                          name="editRoleDisplayName"
                          onChange={(event) => setEditDisplayName(event.target.value)}
                          value={editDisplayName}
                        />
                      </Field>
                      <Field label={translate("Description")}>
                        <textarea
                          className={textAreaClassName}
                          name="editRoleDescription"
                          onChange={(event) => setEditDescription(event.target.value)}
                          value={editDescription}
                        />
                      </Field>
                      <Field label={translate("Permission keys (comma separated)")}>
                        <textarea
                          className={textAreaClassName}
                          name="editRolePermissions"
                          onChange={(event) => setEditPermissions(event.target.value)}
                          value={editPermissions}
                        />
                      </Field>
                      <div className="flex justify-end gap-2">
                        <Button onClick={() => setEditing(undefined)} type="button" variant="ghost">
                          {translate("Cancel")}</Button>
                        <Button disabled={pending !== ""} type="submit">
                          {translate("Save role")}</Button>
                      </div>
                    </form>
                  )}
                </div>
              ))}
              {roles.length === 0 && <EmptyState text={translate("No roles match this view.")} />}
            </div>
          </CardContent>
        </Card>

        <Card data-ui-action="create-role">
          <CardHeader>
            <CardTitle>{translate("Create custom role")}</CardTitle>
            <CardDescription>{translate("Keys are stable; permission sets remain editable.")}</CardDescription>
          </CardHeader>
          <CardContent>
            <form className="grid gap-4 sm:grid-cols-2" onSubmit={submitCreate}>
              <Field label={translate("Role key")}>
                <input
                  className={inputClassName}
                  name="roleKey"
                  onChange={(event) => setKey(event.target.value)}
                  placeholder={translate("release-operator")}
                  value={key}
                />
              </Field>
              <Field label={translate("Display name")}>
                <input
                  className={inputClassName}
                  name="roleDisplayName"
                  onChange={(event) => setDisplayName(event.target.value)}
                  placeholder={translate("Release operator")}
                  value={displayName}
                />
              </Field>
              <Field className="sm:col-span-2" label={translate("Description")}>
                <textarea
                  className={textAreaClassName}
                  name="roleDescription"
                  onChange={(event) => setDescription(event.target.value)}
                  value={description}
                />
              </Field>
              <Field className="sm:col-span-2" label={translate("Permission keys (comma separated)")}>
                <textarea
                  className={textAreaClassName}
                  name="rolePermissions"
                  onChange={(event) => setPermissionKeys(event.target.value)}
                  placeholder={translate("platform.environment.read, platform.environment.update")}
                  value={permissionKeys}
                />
              </Field>
              <div className="sm:col-span-2 sm:justify-self-end">
                <Button disabled={pending !== "" || !isUuid(tenantId) || !isUuid(applicationId)} type="submit">
                  {pending === "create-role" ? (
                    <LoaderCircle className="size-4 animate-spin" />
                  ) : (
                    <Plus className="size-4" />
                  )}
                  {translate("Create role")}</Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>

      <Card data-ui-action="list-permissions">
        <CardHeader>
          <CardTitle>{translate("Permission catalog")}</CardTitle>
          <CardDescription>{translate("Contract-owned keys available to roles and policies.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <SearchInput
            onChange={onPermissionQueryChange}
            placeholder={translate("Search permission keys")}
            value={permissionQuery}
          />
          <ResourceError error={permissionsError} />
          <div className="mt-4 max-h-[48rem] space-y-2 overflow-y-auto pr-1">
            {permissions.map((permission) => (
              <div className="rounded-lg border border-white/7 bg-white/[0.02] p-3" key={permission.key}>
                <p className="break-all font-mono text-[11px] text-violet-300">
                  {permission.key}
                </p>
                <p className="mt-1 text-xs font-medium text-slate-300">
                  {permission.displayName}
                </p>
                <p className="mt-1 text-[11px] leading-5 text-slate-600">
                  {permission.description}
                </p>
              </div>
            ))}
          </div>
          <datalist id="authorization-permission-keys">
            {permissions.map((permission) => (
              <option key={permission.key} value={permission.key} />
            ))}
          </datalist>
        </CardContent>
      </Card>
    </div>
  );
}

function BindingsPanel({
  applicationId,
  actorQuery,
  bindings,
  csrfToken,
  error,
  includeArchived,
  onActorQueryChange,
  onIncludeArchivedChange,
  pending,
  reloadBindings,
  reloadRevisions,
  roles,
  runMutation,
  tenantId,
}: {
  applicationId: string;
  actorQuery: string;
  bindings: RoleBindingRecord[];
  csrfToken: string;
  error: unknown;
  includeArchived: boolean;
  onActorQueryChange: (value: string) => void;
  onIncludeArchivedChange: (value: boolean) => void;
  pending: string;
  reloadBindings: () => Promise<unknown>;
  reloadRevisions: () => Promise<unknown>;
  roles: AuthorizationRoleRecord[];
  runMutation: MutationRunner;
  tenantId: string;
}) {
  const [editing, setEditing] = useState<RoleBindingRecord>();
  const [actorId, setActorId] = useState("");
  const [roleId, setRoleId] = useState("");
  const [scope, setScope] = useState<ScopeFieldsValue>(emptyScope);

  function resetForm() {
    setEditing(undefined);
    setActorId("");
    setRoleId("");
    setScope({ applicationId, environmentId: "", tenantId });
  }

  function beginEdit(binding: RoleBindingRecord) {
    setEditing(binding);
    setActorId(binding.actorId);
    setRoleId(binding.roleId);
    setScope(fromScope(binding.scope));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const bindingId = editing?.id ?? crypto.randomUUID();
    const submittedScope =
      !scope.tenantId && isUuid(tenantId) && isUuid(applicationId)
        ? { applicationId, environmentId: "", tenantId }
        : scope;
    const success = await runMutation(
      editing ? `set-binding-${editing.id}` : "set-binding",
      () =>
        setRoleBinding(csrfToken, bindingId, {
          actorId,
          expectedVersion: editing?.version ?? 0,
          roleId,
          scope: toScope(submittedScope),
        }),
      [reloadBindings, reloadRevisions],
      editing ? "Role binding updated." : "Role binding created.",
    );
    if (success) resetForm();
  }

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.72fr_1.28fr]">
      <Card data-ui-action="set-role-binding">
        <CardHeader>
          <CardTitle>{translate(editing ? "Edit role binding" : "Bind role to actor")}</CardTitle>
          <CardDescription>
            {translate("Bindings default to the selected application; an environment may narrow the grant further.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="space-y-4" onSubmit={submit}>
            <Field label={translate("Actor ID")}>
              <input
                className={inputClassName}
                name="bindingActorId"
                onChange={(event) => setActorId(event.target.value)}
                placeholder={translate("user UUID or service client ID")}
                value={actorId}
              />
            </Field>
            <SearchableSelect
              ariaLabel={translate("Role")}
              className={inputClassName}
              emptyLabel={translate("Select a role")}
              label={translate("Role")}
              labelClassName={labelClassName}
              name="bindingRoleId"
              onChange={setRoleId}
              options={roles
                .filter((role) => role.status === activeStatus)
                .map((role) => ({ label: `${role.displayName} (${role.key})`, value: role.id }))}
              value={roleId}
            />
            <ScopeFields onChange={setScope} prefix="binding" value={scope} />
            <div className="flex justify-end gap-2">
              {editing && (
                <Button onClick={resetForm} type="button" variant="ghost">
                  {translate("Cancel")}</Button>
              )}
              <Button disabled={pending !== ""} type="submit">
                <UserRoundCog className="size-4" />
                {translate(editing ? "Save binding" : "Create binding")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card data-ui-action="list-role-bindings">
        <CardHeader>
          <CardTitle>{translate("Role bindings")}</CardTitle>
          <CardDescription>{translate("Search actors or constrain the view to one tenant.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-2">
            <SearchInput
              onChange={onActorQueryChange}
              placeholder={translate("Filter actor IDs")}
              value={actorQuery}
            />
            <div className="rounded-lg border border-white/8 bg-white/[0.02] px-3 py-2 font-mono text-[10px] text-slate-500">
              {isUuid(applicationId)
                ? `${shortId(tenantId)} / ${shortId(applicationId)}`
                : translate("All scopes")}
            </div>
          </div>
          <label className="mt-3 flex items-center gap-2 text-xs text-slate-500">
            <input
              checked={includeArchived}
              className="accent-violet-400"
              onChange={(event) => onIncludeArchivedChange(event.target.checked)}
              type="checkbox"
            />
            {translate("Include removed bindings")}</label>
          <ResourceError error={error} />
          <div className="mt-4 space-y-3">
            {bindings.map((binding) => (
              <div
                className="rounded-xl border border-white/8 bg-slate-950/45 p-4"
                data-testid={`authorization-binding-${binding.id}`}
                key={binding.id}
              >
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="font-medium text-slate-100">{binding.actorId}</p>
                      <StatusBadge active={binding.status === activeStatus} />
                    </div>
                    <p className="mt-1 font-mono text-xs text-violet-300">
                      {binding.roleKey}
                    </p>
                    <ScopeSummary scope={binding.scope} />
                    <p className="mt-2 text-[11px] text-slate-600">{translate("v")}{binding.version}</p>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button onClick={() => beginEdit(binding)} size="sm" type="button" variant="ghost">
                      {binding.status === activeStatus ? (
                        <Pencil className="size-3.5" />
                      ) : (
                        <RotateCcw className="size-3.5" />
                      )}
                      {translate(binding.status === activeStatus ? "Edit" : "Reactivate")}
                    </Button>
                    {binding.status === activeStatus && (
                      <Button
                        data-ui-action="remove-role-binding"
                        disabled={pending !== ""}
                        onClick={() => {
                          if (!window.confirm(translate(`Remove ${binding.roleKey} from ${binding.actorId}?`))) {
                            return;
                          }
                          void runMutation(
                            `remove-binding-${binding.id}`,
                            () => removeRoleBinding(csrfToken, binding),
                            [reloadBindings, reloadRevisions],
                            "Role binding removed.",
                          );
                        }}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <Trash2 className="size-3.5" /> {translate("Remove")}</Button>
                    )}
                  </div>
                </div>
              </div>
            ))}
            {bindings.length === 0 && <EmptyState text={translate("No role bindings match this view.")} />}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function PoliciesPanel({
  applicationId,
  csrfToken,
  error,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  pending,
  permissions,
  policies,
  query,
  reloadPolicies,
  reloadRevisions,
  runMutation,
  tenantId,
}: {
  applicationId: string;
  csrfToken: string;
  error: unknown;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  pending: string;
  permissions: PermissionRecord[];
  policies: PolicyRuleRecord[];
  query: string;
  reloadPolicies: () => Promise<unknown>;
  reloadRevisions: () => Promise<unknown>;
  runMutation: MutationRunner;
  tenantId: string;
}) {
  const [editing, setEditing] = useState<PolicyRuleRecord>();
  const [name, setName] = useState("");
  const [effect, setEffect] = useState<PolicyEffect>(allowEffect);
  const [subjectType, setSubjectType] = useState<PolicySubjectType>(actorSubject);
  const [subject, setSubject] = useState("");
  const [permission, setPermission] = useState("");
  const [scope, setScope] = useState<ScopeFieldsValue>(emptyScope);
  const [resourceType, setResourceType] = useState("");
  const [resourceId, setResourceId] = useState("");
  const [conditionEnabled, setConditionEnabled] = useState(false);
  const [condition, setCondition] = useState<TargetingRuleDraft>(() => createRuleDraft());

  function resetForm() {
    setEditing(undefined);
    setName("");
    setEffect(allowEffect);
    setSubjectType(actorSubject);
    setSubject("");
    setPermission("");
    setScope(emptyScope);
    setResourceType("");
    setResourceId("");
    setConditionEnabled(false);
    setCondition(createRuleDraft());
  }

  function beginEdit(policy: PolicyRuleRecord) {
    setEditing(policy);
    setName(policy.name);
    setEffect(policy.effect);
    setSubjectType(policy.subjectType);
    setSubject(policy.subject);
    setPermission(policy.permission);
    setScope(fromScope(policy.scope));
    setResourceType(policy.resourceType);
    setResourceId(policy.resourceId);
    setConditionEnabled(Boolean(policy.condition));
    setCondition(createRuleDraft(policy.condition ?? undefined));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const input = {
      condition: conditionEnabled ? toRuleInput(condition) : undefined,
      effect,
      name,
      permission,
      resourceId,
      resourceType,
      scope: toScope({
        applicationId: scope.applicationId || applicationId,
        environmentId: scope.environmentId,
        tenantId: scope.tenantId || tenantId,
      }),
      subject: subjectType === anySubject ? "*" : subject,
      subjectType,
    };
    const success = await runMutation(
      editing ? `update-policy-${editing.id}` : "create-policy",
      () =>
        editing
          ? updatePolicyRule(csrfToken, editing, input)
          : createPolicyRule(csrfToken, input),
      [reloadPolicies, reloadRevisions],
      editing ? "Policy rule updated." : "Policy rule created.",
    );
    if (success) resetForm();
  }

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.72fr_1.28fr]">
      <Card data-ui-action="create-policy-rule">
        <CardHeader>
          <CardTitle>{translate(editing ? "Edit policy rule" : "Create policy rule")}</CardTitle>
          <CardDescription>
            {translate("Explicit denies override every matching grant at the same or broader scope.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="space-y-4" onSubmit={submit}>
            <Field label={translate("Policy name")}>
              <input
                className={inputClassName}
                name="policyName"
                onChange={(event) => setName(event.target.value)}
                value={name}
              />
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={translate("Effect")}>
                <select
                  className={inputClassName}
                  name="policyEffect"
                  onChange={(event) => setEffect(event.target.value as PolicyEffect)}
                  value={effect}
                >
                  <option value={allowEffect}>{translate("Allow")}</option>
                  <option value={denyEffect}>{translate("Deny")}</option>
                </select>
              </Field>
              <Field label={translate("Policy subject type")}>
                <select
                  className={inputClassName}
                  name="policySubjectType"
                  onChange={(event) =>
                    setSubjectType(event.target.value as PolicySubjectType)
                  }
                  value={subjectType}
                >
                  <option value={actorSubject}>{translate("Actor")}</option>
                  <option value={roleSubject}>{translate("Role key or ID")}</option>
                  <option value={anySubject}>{translate("Any actor")}</option>
                </select>
              </Field>
            </div>
            <Field label={translate("Policy subject")}>
              <input
                className={inputClassName}
                disabled={subjectType === anySubject}
                name="policySubject"
                onChange={(event) => setSubject(event.target.value)}
                placeholder={translate(subjectType === anySubject ? "*" : "actor ID or role key")}
                value={subjectType === anySubject ? "*" : subject}
              />
            </Field>
            <Field label={translate("Permission")}>
              <input
                className={inputClassName}
                list="policy-permission-keys"
                name="policyPermission"
                onChange={(event) => setPermission(event.target.value)}
                value={permission}
              />
              <datalist id="policy-permission-keys">
                {permissions.map((item) => (
                  <option key={item.key} value={item.key} />
                ))}
              </datalist>
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={translate("Resource type (ACL, optional)")}>
                <input
                  className={inputClassName}
                  name="policyResourceType"
                  onChange={(event) => setResourceType(event.target.value)}
                  placeholder={translate("order")}
                  value={resourceType}
                />
              </Field>
              <Field label={translate("Resource ID (ACL, optional)")}>
                <input
                  className={inputClassName}
                  disabled={!resourceType}
                  name="policyResourceId"
                  onChange={(event) => setResourceId(event.target.value)}
                  placeholder={translate("order-123; leave blank for all")}
                  value={resourceId}
                />
              </Field>
            </div>
            <div className="rounded-xl border border-white/8 p-3">
              <label className="flex items-center gap-2 text-xs text-slate-400">
                <input
                  checked={conditionEnabled}
                  className="accent-violet-400"
                  onChange={(event) => setConditionEnabled(event.target.checked)}
                  type="checkbox"
                />
                {translate("Enable ABAC attribute condition")}
              </label>
              {conditionEnabled && (
                <div className="mt-4">
                  <RuleEditor
                    draft={condition}
                    idPrefix="authorizationPolicyCondition"
                    onChange={setCondition}
                  />
                </div>
              )}
            </div>
            <ScopeFields onChange={setScope} prefix="policy" value={scope} />
            <div className="flex justify-end gap-2">
              {editing && (
                <Button onClick={resetForm} type="button" variant="ghost">
                  {translate("Cancel")}</Button>
              )}
              <Button disabled={pending !== ""} type="submit">
                <ShieldCheck className="size-4" />
                {translate(editing ? "Save policy" : "Create policy")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card data-ui-action="list-policy-rules">
        <CardHeader>
          <CardTitle>{translate("Policy rules")}</CardTitle>
          <CardDescription>{translate("Fine-grained exceptions layered over role grants.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <SearchInput onChange={onQueryChange} placeholder={translate("Search name or permission")} value={query} />
          <p className="mt-3 font-mono text-[10px] text-slate-500">
            {translate("Selected scope")}: {shortId(tenantId) || "—"} / {shortId(applicationId) || "—"}
          </p>
          <label className="mt-3 flex items-center gap-2 text-xs text-slate-500">
            <input
              checked={includeArchived}
              className="accent-violet-400"
              onChange={(event) => onIncludeArchivedChange(event.target.checked)}
              type="checkbox"
            />
            {translate("Include archived policies")}</label>
          <ResourceError error={error} />
          <div className="mt-4 space-y-3">
            {policies.map((policy) => (
              <div
                className="rounded-xl border border-white/8 bg-slate-950/45 p-4"
                data-testid={`authorization-policy-${policy.id}`}
                key={policy.id}
              >
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="font-medium text-slate-100">{policy.name}</p>
                      <StatusBadge active={policy.status === activeStatus} />
                      <Badge
                        className={cn(
                          policy.effect === denyEffect
                            ? "border-rose-400/20 bg-rose-400/10 text-rose-300"
                            : "border-emerald-400/20 bg-emerald-400/10 text-emerald-300",
                        )}
                      >
                        {translate(policy.effect === denyEffect ? "Deny" : "Allow")}
                      </Badge>
                    </div>
                    <p className="mt-2 break-all font-mono text-xs text-violet-300">
                      {policy.permission}
                    </p>
                    <p className="mt-2 text-xs text-slate-500">
                      {friendlySubjectType(policy.subjectType)}: {policy.subject}
                    </p>
                    {(policy.resourceType || policy.resourceId) && (
                      <p className="mt-2 font-mono text-[10px] text-sky-300">
                        ACL {policy.resourceType || "*"}/{policy.resourceId || "*"}
                      </p>
                    )}
                    {policy.condition && (
                      <p className="mt-2 text-[10px] text-amber-300">
                        ABAC · {policy.condition.conditions.length} {translate("condition(s)")}
                      </p>
                    )}
                    <ScopeSummary scope={policy.scope} />
                    <p className="mt-2 text-[11px] text-slate-600">{translate("v")}{policy.version}</p>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button
                      data-ui-action="update-policy-rule"
                      onClick={() => beginEdit(policy)}
                      size="sm"
                      type="button"
                      variant="ghost"
                    >
                      <Pencil className="size-3.5" /> {translate("Edit")}</Button>
                    {policy.status === activeStatus ? (
                      <Button
                        data-ui-action="archive-policy-rule"
                        disabled={pending !== ""}
                        onClick={() => {
                          if (!window.confirm(translate(`Archive policy ${policy.name}?`))) return;
                          void runMutation(
                            `archive-policy-${policy.id}`,
                            () => archivePolicyRule(csrfToken, policy),
                            [reloadPolicies, reloadRevisions],
                            "Policy rule archived.",
                          );
                        }}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <Archive className="size-3.5" /> {translate("Archive")}</Button>
                    ) : (
                      <Button
                        data-ui-action="restore-policy-rule"
                        disabled={pending !== ""}
                        onClick={() =>
                          void runMutation(
                            `restore-policy-${policy.id}`,
                            () => restorePolicyRule(csrfToken, policy),
                            [reloadPolicies, reloadRevisions],
                            "Policy rule restored.",
                          )
                        }
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <RotateCcw className="size-3.5" /> {translate("Restore")}</Button>
                    )}
                  </div>
                </div>
              </div>
            ))}
            {policies.length === 0 && <EmptyState text={translate("No policy rules match this view.")} />}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function SimulatorPanel({
  actorId,
  applicationId,
  csrfToken,
  onRevisionResourceIdChange,
  onRevisionResourceTypeChange,
  pending,
  permissions,
  revisionResourceId,
  revisionResourceType,
  revisions,
  revisionsError,
  runMutation,
  tenantId,
}: {
  actorId: string;
  applicationId: string;
  csrfToken: string;
  onRevisionResourceIdChange: (value: string) => void;
  onRevisionResourceTypeChange: (value: string) => void;
  pending: string;
  permissions: PermissionRecord[];
  revisionResourceId: string;
  revisionResourceType: string;
  revisions: Array<{
    changeSummary: string;
    changeType: string;
    createdAt: string;
    createdBy: string;
    id: string;
    resourceId: string;
    resourceType: string;
    revisionNumber: number;
    snapshotHash: string;
  }>;
  revisionsError: unknown;
  runMutation: MutationRunner;
  tenantId: string;
}) {
  const [simulatedActor, setSimulatedActor] = useState(actorId);
  const [permission, setPermission] = useState("platform.info.read");
  const [trustedRoles, setTrustedRoles] = useState("");
  const [scope, setScope] = useState<ScopeFieldsValue>(emptyScope);
  const [resourceType, setResourceType] = useState("");
  const [resourceId, setResourceId] = useState("");
  const [attributesJson, setAttributesJson] = useState(
    '{\n  "subject.department": "finance",\n  "resource.amount": 1000,\n  "context.mfa": true\n}',
  );
  const [simulation, setSimulation] = useState<AuthorizationDecisionRecord>();
  const [currentDecision, setCurrentDecision] = useState<AuthorizationDecisionRecord>();

  async function submitSimulation(event: FormEvent) {
    event.preventDefault();
    await runMutation(
      "simulate-authorization",
      async () => {
        setSimulation(
          await simulateAuthorization(csrfToken, {
            actorId: simulatedActor,
            attributes: parseAuthorizationAttributes(attributesJson),
            permission,
            resourceId,
            resourceType,
            scope: toScope({
              applicationId: scope.applicationId || applicationId,
              environmentId: scope.environmentId,
              tenantId: scope.tenantId || tenantId,
            }),
            trustedRoles: parseCsv(trustedRoles),
          }),
        );
      },
      [],
      "Authorization simulation complete.",
    );
  }

  async function checkCurrentActor() {
    await runMutation(
      "check-permission",
      async () => {
        setCurrentDecision(
          await checkCurrentActorPermission(csrfToken, {
            actorId,
            attributes: [],
            permission,
            resourceId,
            resourceType,
            scope: toScope({
              applicationId: scope.applicationId || applicationId,
              environmentId: scope.environmentId,
              tenantId: scope.tenantId || tenantId,
            }),
          }),
        );
      },
      [],
      "Current actor permission checked.",
    );
  }

  return (
    <div className="grid items-start gap-5 xl:grid-cols-2">
      <Card data-ui-action="simulate-authorization">
        <CardHeader>
          <CardTitle>{translate("Decision simulator")}</CardTitle>
          <CardDescription>
            {translate("Test any actor and optional trusted Passport role without changing policy.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="space-y-4" onSubmit={submitSimulation}>
            <Field label={translate("Actor ID")}>
              <input
                className={inputClassName}
                name="simulationActorId"
                onChange={(event) => setSimulatedActor(event.target.value)}
                value={simulatedActor}
              />
            </Field>
            <Field label={translate("Permission")}>
              <input
                className={inputClassName}
                list="simulation-permission-keys"
                name="simulationPermission"
                onChange={(event) => setPermission(event.target.value)}
                value={permission}
              />
              <datalist id="simulation-permission-keys">
                {permissions.map((item) => (
                  <option key={item.key} value={item.key} />
                ))}
              </datalist>
            </Field>
            <Field label={translate("Trusted roles (simulation only, comma separated)")}>
              <input
                className={inputClassName}
                name="simulationTrustedRoles"
                onChange={(event) => setTrustedRoles(event.target.value)}
                placeholder={translate("SuperAdministrator")}
                value={trustedRoles}
              />
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={translate("Resource type (ACL)")}>
                <input
                  className={inputClassName}
                  name="simulationResourceType"
                  onChange={(event) => setResourceType(event.target.value)}
                  placeholder={translate("order")}
                  value={resourceType}
                />
              </Field>
              <Field label={translate("Resource ID (ACL)")}>
                <input
                  className={inputClassName}
                  name="simulationResourceId"
                  onChange={(event) => setResourceId(event.target.value)}
                  placeholder={translate("order-123")}
                  value={resourceId}
                />
              </Field>
            </div>
            <Field label={translate("Trusted ABAC attributes (JSON object, simulation only)")}>
              <textarea
                className={cn(textAreaClassName, "h-32 font-mono text-xs")}
                name="simulationAttributes"
                onChange={(event) => setAttributesJson(event.target.value)}
                value={attributesJson}
              />
            </Field>
            <ScopeFields onChange={setScope} prefix="simulation" value={scope} />
            <p className="text-xs leading-5 text-slate-500">
              {translate("Production ABAC attributes must be supplied by an application-bound confidential client. Check my access intentionally omits custom attributes.")}
            </p>
            <div className="flex flex-wrap justify-end gap-2">
              <Button
                data-ui-action="check-permission"
                disabled={pending !== ""}
                onClick={() => void checkCurrentActor()}
                type="button"
                variant="outline"
              >
                <KeyRound className="size-4" /> {translate("Check my access")}</Button>
              <Button disabled={pending !== ""} type="submit">
                <Play className="size-4" /> {translate("Simulate")}</Button>
            </div>
          </form>
          <div className="mt-5 grid gap-3 sm:grid-cols-2">
            <DecisionCard decision={simulation} label={translate("Simulation")} />
            <DecisionCard decision={currentDecision} label={translate("Current actor")} />
          </div>
        </CardContent>
      </Card>

      <Card data-ui-action="list-policy-revisions">
        <CardHeader>
          <CardTitle>{translate("Policy revisions")}</CardTitle>
          <CardDescription>{translate("Immutable history for roles, bindings, and policy rules.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-2">
            <select
              className={inputClassName}
              name="revisionResourceType"
              onChange={(event) => onRevisionResourceTypeChange(event.target.value)}
              value={revisionResourceType}
            >
              <option value="">{translate("All resource types")}</option>
              <option value="role">{translate("Role")}</option>
              <option value="role_binding">{translate("Role binding")}</option>
              <option value="policy_rule">{translate("Policy rule")}</option>
              <option value="permission">{translate("Permission")}</option>
            </select>
            <input
              className={inputClassName}
              name="revisionResourceId"
              onChange={(event) => onRevisionResourceIdChange(event.target.value)}
              placeholder={translate("Resource ID (optional)")}
              value={revisionResourceId}
            />
          </div>
          <ResourceError error={revisionsError} />
          <div className="mt-4 max-h-[44rem] space-y-3 overflow-y-auto pr-1">
            {revisions.map((revision) => (
              <div className="rounded-xl border border-white/8 bg-slate-950/45 p-4" key={revision.id}>
                <div className="flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2">
                    <FileClock className="size-4 text-violet-300" />
                    <p className="text-sm font-medium text-slate-200">
                      {translate("Revision")} {revision.revisionNumber}
                    </p>
                  </div>
                  <Badge variant="planned">{revision.changeType}</Badge>
                </div>
                <p className="mt-2 text-xs text-slate-400">{revision.changeSummary}</p>
                <p className="mt-2 break-all font-mono text-[10px] text-slate-600">
                  {revision.resourceType}/{revision.resourceId}
                </p>
                <p className="mt-2 text-[10px] text-slate-600">
                  {revision.createdBy} · {formatTime(revision.createdAt)}
                </p>
              </div>
            ))}
            {revisions.length === 0 && <EmptyState text={translate("No policy revisions match this view.")} />}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function ScopeFields({
  onChange,
  prefix,
  value,
}: {
  onChange: (value: ScopeFieldsValue) => void;
  prefix: string;
  value: ScopeFieldsValue;
}) {
  const tenants = useSWR("authorization-field-tenants", () => listTenants(platformPage));
  const applications = useSWR(
    value.tenantId ? ["authorization-field-applications", value.tenantId] : null,
    () => listApplications(value.tenantId, platformPage),
  );
  const environments = useSWR(
    value.tenantId && value.applicationId
      ? ["authorization-field-environments", value.tenantId, value.applicationId]
      : null,
    () => listEnvironments(value.tenantId, value.applicationId, platformPage),
  );

  return (
    <fieldset className="rounded-xl border border-white/8 p-3">
      <legend className="px-1 text-[10px] font-semibold uppercase tracking-[0.15em] text-slate-600">
        {translate("Scope (optional)")}</legend>
      <div className="grid gap-3">
        <SearchableSelect
          ariaLabel={translate("Tenant")}
          className={inputClassName}
          emptyLabel={translate("Global")}
          label={translate("Tenant")}
          labelClassName={labelClassName}
          name={`${prefix}TenantId`}
          onChange={(tenantId) => onChange({ applicationId: "", environmentId: "", tenantId })}
          options={(tenants.data?.tenants ?? []).map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))}
          value={value.tenantId}
        />
        <SearchableSelect
          ariaLabel={translate("Application")}
          className={inputClassName}
          disabled={!value.tenantId}
          emptyLabel={translate("None")}
          label={translate("Application")}
          labelClassName={labelClassName}
          name={`${prefix}ApplicationId`}
          onChange={(applicationId) => onChange({ ...value, applicationId, environmentId: "" })}
          options={(applications.data?.applications ?? []).map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))}
          value={value.applicationId}
        />
        <SearchableSelect
          ariaLabel={translate("Environment")}
          className={inputClassName}
          disabled={!value.applicationId}
          emptyLabel={translate("None")}
          label={translate("Environment")}
          labelClassName={labelClassName}
          name={`${prefix}EnvironmentId`}
          onChange={(environmentId) => onChange({ ...value, environmentId })}
          options={(environments.data?.environments ?? []).map((environment) => ({ label: `${environment.displayName} (${environment.slug})`, value: environment.id }))}
          value={value.environmentId}
        />
        <ResourceError error={tenants.error ?? applications.error ?? environments.error} />
      </div>
    </fieldset>
  );
}

function ScopeSummary({ scope }: { scope: AuthorizationScopeInput }) {
  const parts = [
    scope.tenantId ? `tenant:${shortId(scope.tenantId)}` : "global",
    scope.applicationId ? `app:${shortId(scope.applicationId)}` : "",
    scope.environmentId ? `env:${shortId(scope.environmentId)}` : "",
  ].filter(Boolean);
  return <p className="mt-2 font-mono text-[10px] text-slate-500">{parts.join(" / ")}</p>;
}

function DecisionCard({
  decision,
  label,
}: {
  decision?: AuthorizationDecisionRecord;
  label: string;
}) {
  return (
    <div className="rounded-xl border border-white/8 bg-slate-950/55 p-4">
      <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-600">{label}</p>
      {!decision ? (
        <p className="mt-3 text-xs text-slate-600">{translate("No decision yet.")}</p>
      ) : (
        <>
          <div className="mt-3 flex items-center gap-2">
            {decision.allowed ? (
              <CheckCircle2 className="size-4 text-emerald-300" />
            ) : (
              <CircleAlert className="size-4 text-rose-300" />
            )}
            <p
              className={cn(
                "text-sm font-semibold",
                decision.allowed ? "text-emerald-300" : "text-rose-300",
              )}
            >
              {translate(decision.allowed ? "Allowed" : "Denied")}
            </p>
          </div>
          <p className="mt-2 text-xs leading-5 text-slate-500">{translate(decision.reason)}</p>
          {decision.matchedRoleKeys.length > 0 && (
            <p className="mt-2 font-mono text-[10px] text-violet-300">
              {decision.matchedRoleKeys.join(", ")}
            </p>
          )}
        </>
      )}
    </div>
  );
}

function FilterBar({
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  query,
  queryPlaceholder,
}: {
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  query: string;
  queryPlaceholder: string;
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <SearchInput onChange={onQueryChange} placeholder={queryPlaceholder} value={query} />
      </div>
      <label className="flex shrink-0 items-center gap-2 text-xs text-slate-500">
        <input
          checked={includeArchived}
          className="accent-violet-400"
          onChange={(event) => onIncludeArchivedChange(event.target.checked)}
          type="checkbox"
        />
        {translate("Include archived")}</label>
    </div>
  );
}

function SearchInput({
  onChange,
  placeholder,
  value,
}: {
  onChange: (value: string) => void;
  placeholder: string;
  value: string;
}) {
  return (
    <label className="relative block">
      <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-600" />
      <input
        className={cn(inputClassName, "pl-9")}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        type="search"
        value={value}
      />
    </label>
  );
}

function Field({
  children,
  className,
  label,
}: {
  children: ReactNode;
  className?: string;
  label: string;
}) {
  return (
    <label className={className}>
      <span className={labelClassName}>{label}</span>
      {children}
    </label>
  );
}

function StatusBadge({ active }: { active: boolean }) {
  return (
    <Badge variant={active ? "success" : "planned"}>{translate(active ? "Active" : "Archived")}</Badge>
  );
}

function ResourceError({ error }: { error: unknown }) {
  if (!error) return null;
  return (
    <div className="mt-4 flex items-start gap-2 rounded-xl border border-rose-400/15 bg-rose-400/[0.06] p-3 text-xs text-rose-300">
      <CircleAlert className="mt-0.5 size-4 shrink-0" />
      {translate(authorizationErrorMessage(error))}
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return (
    <div className="rounded-xl border border-dashed border-white/10 p-8 text-center text-sm text-slate-600">
      {text}
    </div>
  );
}

function parseCsv(value: string): string[] {
  return value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function parseAuthorizationAttributes(
  value: string,
): Array<{ key: string; value: TargetingValueInput }> {
  if (!value.trim()) return [];

  const parsed: unknown = JSON.parse(value);
  if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
    throw new Error("ABAC attributes must be a JSON object.");
  }

  return Object.entries(parsed).map(([key, attributeValue]) => {
    if (typeof attributeValue === "string") {
      return { key, value: { text: attributeValue } };
    }
    if (typeof attributeValue === "boolean") {
      return { key, value: { truth: attributeValue } };
    }
    if (typeof attributeValue === "number" && Number.isFinite(attributeValue)) {
      return { key, value: { numeric: attributeValue } };
    }
    throw new Error(`ABAC attribute ${key} must be a string, boolean, or finite number.`);
  });
}

function toScope(scope: ScopeFieldsValue): AuthorizationScopeInput {
  return {
    applicationId: scope.applicationId.trim() || undefined,
    environmentId: scope.environmentId.trim() || undefined,
    tenantId: scope.tenantId.trim() || undefined,
  };
}

function fromScope(scope: AuthorizationScopeInput): ScopeFieldsValue {
  return {
    applicationId: scope.applicationId ?? "",
    environmentId: scope.environmentId ?? "",
    tenantId: scope.tenantId ?? "",
  };
}

function friendlySubjectType(subjectType: PolicySubjectType): string {
  if (subjectType === roleSubject) return "Role";
  if (subjectType === anySubject) return "Any actor";
  return "Actor";
}

function shortId(value: string): string {
  return value.length > 12 ? `${value.slice(0, 8)}…` : value;
}

function isUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
    value,
  );
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
