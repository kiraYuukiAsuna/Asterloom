"use client";

import {
  Archive,
  AppWindow,
  Ban,
  Check,
  CircleAlert,
  Clipboard,
  Eye,
  KeyRound,
  LoaderCircle,
  Pencil,
  Plus,
  RefreshCcw,
  RotateCcw,
  Search,
  ShieldCheck,
  Trash2,
  UserPlus,
  Users,
} from "lucide-react";
import { type FormEvent, type ReactNode, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SearchableMultiSelect } from "@/components/ui/searchable-multi-select";
import { SearchableSelect } from "@/components/ui/searchable-select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  archiveUser,
  createUser,
  createClient,
  createScope,
  deleteClient,
  deleteScope,
  getClient,
  getScope,
  getUser,
  identityErrorMessage,
  inviteUser,
  listApplicationMemberships,
  listClients,
  listScopes,
  listUsers,
  listUserSessions,
  passportRoles,
  reactivateUser,
  removeApplicationMembership,
  resetUserPassword,
  resendInvitation,
  restoreUser,
  revokeAllUserSessions,
  revokeUserSession,
  rotateClientSecret,
  setUserRoles,
  setApplicationMembership,
  suspendUser,
  updateClient,
  updateScope,
  updateUser,
  type ApplicationMembershipRecord,
  type IdentitySessionRecord,
  type IdentityUserRecord,
  type OidcApplicationType,
  type OidcClientRecord,
  type OidcClientType,
  type OidcGrantType,
  type OidcScopeRecord,
  type PassportRole,
} from "@/lib/api/identity-management";
import { listApplications, listTenants } from "@/lib/api/platform-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";
import { translate } from "@/lib/i18n/locale";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-cyan-400/45 focus:ring-2 focus:ring-cyan-400/15 disabled:opacity-50";
const textAreaClassName = cn(inputClassName, "h-24 resize-y py-2 font-mono text-xs");
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";

const pendingStatus = "IDENTITY_USER_STATUS_PENDING";
const activeStatus = "IDENTITY_USER_STATUS_ACTIVE";
const suspendedStatus = "IDENTITY_USER_STATUS_SUSPENDED";
const archivedStatus = "IDENTITY_USER_STATUS_ARCHIVED";
const validSession = "IDENTITY_SESSION_STATUS_VALID";
const publicClient = "OIDC_CLIENT_TYPE_PUBLIC";
const confidentialClient = "OIDC_CLIENT_TYPE_CONFIDENTIAL";
const webApplication = "OIDC_APPLICATION_TYPE_WEB";
const nativeApplication = "OIDC_APPLICATION_TYPE_NATIVE";
const authorizationCode = "OIDC_GRANT_TYPE_AUTHORIZATION_CODE";
const clientCredentials = "OIDC_GRANT_TYPE_CLIENT_CREDENTIALS";
const refreshToken = "OIDC_GRANT_TYPE_REFRESH_TOKEN";
const removedMembership = "APPLICATION_MEMBERSHIP_STATUS_REMOVED";
const platformPage = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

type WorkspaceTab = "users" | "memberships" | "clients" | "scopes";
type Reveal = {
  expiresAt?: string;
  label: string;
  value: string;
};
type MutationRunner = <T>(
  key: string,
  work: () => Promise<T>,
  refresh: Array<() => Promise<unknown>>,
  successMessage: string,
) => Promise<T | undefined>;

export function IdentityWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const [activeTab, setActiveTab] = useState<WorkspaceTab>("users");
  const [pending, setPending] = useState("");
  const [reveal, setReveal] = useState<Reveal>();
  const [userQuery, setUserQuery] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [selectedUserId, setSelectedUserId] = useState("");
  const [membershipUserId, setMembershipUserId] = useState("");
  const [membershipTenantId, setMembershipTenantId] = useState("");
  const [membershipApplicationId, setMembershipApplicationId] = useState("");
  const [includeRemovedMemberships, setIncludeRemovedMemberships] = useState(false);
  const [clientQuery, setClientQuery] = useState("");
  const [selectedClientId, setSelectedClientId] = useState("");
  const [scopeQuery, setScopeQuery] = useState("");
  const [selectedScopeId, setSelectedScopeId] = useState("");

  const users = useSWR(
    ["identity-users", userQuery, includeArchived],
    () => listUsers({ includeArchived, pageSize: 100, query: userQuery }),
    { keepPreviousData: true },
  );
  const user = useSWR(
    selectedUserId ? ["identity-user", selectedUserId] : null,
    () => getUser(selectedUserId),
  );
  const sessions = useSWR(
    selectedUserId ? ["identity-user-sessions", selectedUserId] : null,
    () => listUserSessions(selectedUserId, { includeRevoked: true, pageSize: 100 }),
  );
  const memberships = useSWR(
    [
      "identity-application-memberships",
      membershipUserId,
      membershipTenantId,
      membershipApplicationId,
      includeRemovedMemberships,
    ],
    () =>
      listApplicationMemberships({
        applicationId: membershipApplicationId,
        includeRemoved: includeRemovedMemberships,
        pageSize: 100,
        tenantId: membershipTenantId,
        userId: membershipUserId,
      }),
    { keepPreviousData: true },
  );
  const clients = useSWR(
    ["identity-clients", clientQuery],
    () => listClients({ pageSize: 100, query: clientQuery }),
    { keepPreviousData: true },
  );
  const client = useSWR(
    selectedClientId ? ["identity-client", selectedClientId] : null,
    () => getClient(selectedClientId),
  );
  const scopes = useSWR(
    "identity-scopes",
    () => listScopes({ pageSize: 100, query: "" }),
    { keepPreviousData: true },
  );
  const scope = useSWR(
    selectedScopeId ? ["identity-scope", selectedScopeId] : null,
    () => getScope(selectedScopeId),
  );
  const visibleScopes = (scopes.data?.scopes ?? []).filter((item) => {
    const query = scopeQuery.trim().toLowerCase();
    return !query || `${item.name} ${item.displayName} ${item.description}`.toLowerCase().includes(query);
  });

  async function runMutation<T>(
    key: string,
    work: () => Promise<T>,
    refresh: Array<() => Promise<unknown>>,
    successMessage: string,
  ): Promise<T | undefined> {
    setPending(key);
    try {
      const result = await work();
      await Promise.all(refresh.map((reload) => reload()));
      toast.success(translate(successMessage));
      return result;
    } catch (error) {
      await Promise.allSettled(refresh.map((reload) => reload()));
      toast.error(translate(identityErrorMessage(error)));
      return undefined;
    } finally {
      setPending("");
    }
  }

  const tabs: Array<{ id: WorkspaceTab; label: string; icon: typeof Users }> = [
    { id: "users", label: "Users & sessions", icon: Users },
    { id: "memberships", label: "Application memberships", icon: AppWindow },
    { id: "clients", label: "OIDC clients", icon: KeyRound },
    { id: "scopes", label: "API scopes", icon: ShieldCheck },
  ];

  return (
    <div
      aria-busy={!hydrated}
      className={cn("space-y-6", !hydrated && "pointer-events-none")}
      data-hydrated={hydrated ? "true" : "false"}
      data-identity-workspace
    >
      <section className="theme-hero-cyan overflow-hidden rounded-3xl border border-cyan-300/10 bg-[linear-gradient(135deg,rgba(6,182,212,0.14),rgba(15,23,42,0.86)_55%,rgba(2,6,23,0.97))] p-6 sm:p-8">
        <Badge className="border-cyan-400/20 bg-cyan-400/10 text-cyan-300">
          <ShieldCheck aria-hidden="true" className="size-3" />
          {translate("Passport administration")}</Badge>
        <h1 className="mt-4 text-3xl font-semibold tracking-[-0.035em] text-white sm:text-4xl">
          {translate("Identity control center")}</h1>
        <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-400">
          {translate("Invite and govern users, terminate durable OIDC sessions, and manage every client, grant, redirect URI, scope, and secret exposed by Passport.")}</p>
      </section>

      {reveal && (
        <CredentialReveal onClose={() => setReveal(undefined)} reveal={reveal} />
      )}

      <nav
        aria-label={translate("Identity workspace")}
        className="grid gap-2 rounded-2xl border border-white/8 bg-white/[0.025] p-2 sm:grid-cols-2 xl:grid-cols-4"
      >
        {tabs.map((tab) => {
          const Icon = tab.icon;
          return (
            <button
              className={cn(
                "flex h-11 items-center justify-center gap-2 rounded-xl px-3 text-sm font-medium transition",
                activeTab === tab.id
                  ? "bg-cyan-400/15 text-cyan-200 ring-1 ring-cyan-300/20"
                  : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
              )}
              data-testid={`identity-tab-${tab.id}`}
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              type="button"
            >
              <Icon aria-hidden="true" className="size-4" /> {translate(tab.label)}
            </button>
          );
        })}
      </nav>

      {activeTab === "users" && (
        <UsersPanel
          csrfToken={csrfToken}
          error={users.error}
          includeArchived={includeArchived}
          onIncludeArchivedChange={setIncludeArchived}
          onQueryChange={setUserQuery}
          onReveal={setReveal}
          onSelectUser={setSelectedUserId}
          pending={pending}
          query={userQuery}
          reloadSessions={sessions.mutate}
          reloadUser={user.mutate}
          reloadUsers={users.mutate}
          runMutation={runMutation}
          selectedUser={user.data}
          sessions={sessions.data?.sessions ?? []}
          users={users.data?.users ?? []}
        />
      )}
      {activeTab === "clients" && (
        <ClientsPanel
          clients={clients.data?.clients ?? []}
          csrfToken={csrfToken}
          error={clients.error}
          onQueryChange={setClientQuery}
          onReveal={setReveal}
          onSelectClient={setSelectedClientId}
          pending={pending}
          query={clientQuery}
          reloadClient={client.mutate}
          reloadClients={clients.mutate}
          runMutation={runMutation}
          scopeNames={(scopes.data?.scopes ?? []).map((item) => item.name)}
          selectedClient={client.data}
        />
      )}
      {activeTab === "memberships" && (
        <MembershipsPanel
          applicationId={membershipApplicationId}
          csrfToken={csrfToken}
          error={memberships.error}
          includeRemoved={includeRemovedMemberships}
          memberships={memberships.data?.memberships ?? []}
          onApplicationIdChange={setMembershipApplicationId}
          onIncludeRemovedChange={setIncludeRemovedMemberships}
          onTenantIdChange={(tenantId) => {
            setMembershipTenantId(tenantId);
            setMembershipApplicationId("");
          }}
          onUserIdChange={setMembershipUserId}
          pending={pending}
          reloadMemberships={memberships.mutate}
          runMutation={runMutation}
          tenantId={membershipTenantId}
          userId={membershipUserId}
        />
      )}
      {activeTab === "scopes" && (
        <ScopesPanel
          csrfToken={csrfToken}
          error={scopes.error}
          onQueryChange={setScopeQuery}
          onSelectScope={setSelectedScopeId}
          pending={pending}
          query={scopeQuery}
          reloadScope={scope.mutate}
          reloadScopes={scopes.mutate}
          runMutation={runMutation}
          scopes={visibleScopes}
          selectedScope={scope.data}
        />
      )}
    </div>
  );
}

function UsersPanel({
  csrfToken,
  error,
  includeArchived,
  onIncludeArchivedChange,
  onQueryChange,
  onReveal,
  onSelectUser,
  pending,
  query,
  reloadSessions,
  reloadUser,
  reloadUsers,
  runMutation,
  selectedUser,
  sessions,
  users,
}: {
  csrfToken: string;
  error: unknown;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  onQueryChange: (value: string) => void;
  onReveal: (reveal: Reveal) => void;
  onSelectUser: (id: string) => void;
  pending: string;
  query: string;
  reloadSessions: () => Promise<unknown>;
  reloadUser: () => Promise<unknown>;
  reloadUsers: () => Promise<unknown>;
  runMutation: MutationRunner;
  selectedUser?: IdentityUserRecord;
  sessions: IdentitySessionRecord[];
  users: IdentityUserRecord[];
}) {
  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.85fr_1.15fr]">
      <div className="space-y-5">
        <CreateAccountCard
          csrfToken={csrfToken}
          pending={pending}
          reloadUsers={reloadUsers}
          runMutation={runMutation}
        />
        <InviteUserCard
          csrfToken={csrfToken}
          onReveal={onReveal}
          pending={pending}
          reloadUsers={reloadUsers}
          runMutation={runMutation}
        />
        <Card data-ui-action="list-users">
          <CardHeader>
            <CardTitle>{translate("Passport users")}</CardTitle>
            <CardDescription>{translate("Search active, invited, suspended, and archived accounts.")}</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <SearchInput onChange={onQueryChange} placeholder={translate("Name or email")} value={query} />
              <label className="flex shrink-0 items-center gap-2 text-xs text-slate-500">
                <input
                  checked={includeArchived}
                  className="accent-cyan-400"
                  onChange={(event) => onIncludeArchivedChange(event.target.checked)}
                  type="checkbox"
                />
                {translate("Include archived")}</label>
            </div>
            <ResourceError error={error} />
            <div className="mt-4 max-h-[48rem] space-y-2 overflow-y-auto pr-1">
              {users.map((user) => (
                <button
                  className="flex w-full items-center gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-3 text-left transition hover:border-cyan-300/20 hover:bg-cyan-300/[0.04]"
                  data-ui-action="get-user"
                  key={user.id}
                  onClick={() => onSelectUser(user.id)}
                  type="button"
                >
                  <div className="grid size-9 shrink-0 place-items-center rounded-lg bg-cyan-400/10 text-sm font-semibold text-cyan-300">
                    {user.displayName.slice(0, 1).toUpperCase()}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-slate-200">{user.displayName}</p>
                    <p className="truncate text-xs text-slate-500">{user.email}</p>
                  </div>
                  <UserStatusBadge status={user.status} />
                </button>
              ))}
              {users.length === 0 && <EmptyState text={translate("No users match this view.")} />}
            </div>
          </CardContent>
        </Card>
      </div>

      {selectedUser ? (
        <UserInspector
          csrfToken={csrfToken}
          key={`${selectedUser.id}-${selectedUser.version}`}
          onReveal={onReveal}
          pending={pending}
          reloadSessions={reloadSessions}
          reloadUser={reloadUser}
          reloadUsers={reloadUsers}
          runMutation={runMutation}
          sessions={sessions}
          user={selectedUser}
        />
      ) : (
        <SelectionPlaceholder text={translate("Select a user to load its authoritative detail and sessions.")} />
      )}
    </div>
  );
}

function CreateAccountCard({
  csrfToken,
  pending,
  reloadUsers,
  runMutation,
}: {
  csrfToken: string;
  pending: string;
  reloadUsers: () => Promise<unknown>;
  runMutation: MutationRunner;
}) {
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [emailConfirmed, setEmailConfirmed] = useState(true);
  const [roles, setRoles] = useState<PassportRole[]>([]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const created = await runMutation(
      "create-user",
      () =>
        createUser(csrfToken, {
          displayName,
          email,
          emailConfirmed,
          password,
          roles,
        }),
      [reloadUsers],
      "Passport account created.",
    );
    if (!created) return;
    setEmail("");
    setDisplayName("");
    setPassword("");
    setRoles([]);
  }

  return (
    <Card data-ui-action="create-user">
      <CardHeader>
        <CardTitle>{translate("Create Passport account")}</CardTitle>
        <CardDescription>
          {translate("Create a global account directly. Business users normally register through their trusted application backend.")}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-3" onSubmit={submit}>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={translate("Email")}>
              <input
                className={inputClassName}
                name="createUserEmail"
                onChange={(event) => setEmail(event.target.value)}
                required
                type="email"
                value={email}
              />
            </Field>
            <Field label={translate("Display name")}>
              <input
                className={inputClassName}
                name="createUserDisplayName"
                onChange={(event) => setDisplayName(event.target.value)}
                required
                value={displayName}
              />
            </Field>
          </div>
          <Field label={translate("Initial password")}>
            <input
              autoComplete="new-password"
              className={inputClassName}
              minLength={12}
              name="createUserPassword"
              onChange={(event) => setPassword(event.target.value)}
              required
              type="password"
              value={password}
            />
          </Field>
          <label className="flex items-center gap-2 text-xs text-slate-500">
            <input
              checked={emailConfirmed}
              className="accent-cyan-400"
              onChange={(event) => setEmailConfirmed(event.target.checked)}
              type="checkbox"
            />
            {translate("Mark email as confirmed")}
          </label>
          <RoleChecks onChange={setRoles} roles={roles} />
          <p className="text-xs leading-5 text-slate-600">
            {translate("Leave Passport roles empty for normal business users. These roles are reserved for control-plane operators.")}
          </p>
          <div className="flex justify-end">
            <Button disabled={pending !== ""} type="submit">
              {pending === "create-user" ? (
                <LoaderCircle className="size-4 animate-spin" />
              ) : (
                <UserPlus className="size-4" />
              )}
              {translate("Create account")}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function InviteUserCard({
  csrfToken,
  onReveal,
  pending,
  reloadUsers,
  runMutation,
}: {
  csrfToken: string;
  onReveal: (reveal: Reveal) => void;
  pending: string;
  reloadUsers: () => Promise<unknown>;
  runMutation: MutationRunner;
}) {
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [roles, setRoles] = useState<PassportRole[]>(["Viewer"]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const invitation = await runMutation(
      "invite-user",
      () => inviteUser(csrfToken, { displayName, email, roles }),
      [reloadUsers],
      "User invited.",
    );
    if (!invitation) return;
    onReveal({
      expiresAt: invitation.expiresAt,
      label: `Invitation for ${invitation.user.email}`,
      value: invitation.invitationUrl,
    });
    setEmail("");
    setDisplayName("");
    setRoles(["Viewer"]);
  }

  return (
    <Card data-ui-action="invite-user">
      <CardHeader>
        <CardTitle>{translate("Invite user")}</CardTitle>
        <CardDescription>{translate("The activation URL is displayed once for secure delivery.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-3" onSubmit={submit}>
          <Field label={translate("Email")}>
            <input
              className={inputClassName}
              name="inviteEmail"
              onChange={(event) => setEmail(event.target.value)}
              required
              type="email"
              value={email}
            />
          </Field>
          <Field label={translate("Display name")}>
            <input
              className={inputClassName}
              name="inviteDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              required
              value={displayName}
            />
          </Field>
          <RoleChecks onChange={setRoles} roles={roles} />
          <div className="flex justify-end">
            <Button disabled={pending !== ""} type="submit">
              {pending === "invite-user" ? (
                <LoaderCircle className="size-4 animate-spin" />
              ) : (
                <UserPlus className="size-4" />
              )}
              {translate("Send invitation")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function UserInspector({
  csrfToken,
  onReveal,
  pending,
  reloadSessions,
  reloadUser,
  reloadUsers,
  runMutation,
  sessions,
  user,
}: {
  csrfToken: string;
  onReveal: (reveal: Reveal) => void;
  pending: string;
  reloadSessions: () => Promise<unknown>;
  reloadUser: () => Promise<unknown>;
  reloadUsers: () => Promise<unknown>;
  runMutation: MutationRunner;
  sessions: IdentitySessionRecord[];
  user: IdentityUserRecord;
}) {
  const [displayName, setDisplayName] = useState(user.displayName);
  const [roles, setRoles] = useState<PassportRole[]>(user.roles);
  const [newPassword, setNewPassword] = useState("");
  const refresh = [reloadUsers, reloadUser];

  async function lifecycle(
    key: string,
    action: () => Promise<IdentityUserRecord>,
    message: string,
  ) {
    await runMutation(key, action, [...refresh, reloadSessions], message);
  }

  return (
    <div className="space-y-5">
      <Card data-ui-action="update-user">
        <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{user.displayName}</CardTitle>
            <CardDescription>{user.email}</CardDescription>
          </div>
          <div className="flex flex-wrap gap-1.5">
            <Badge variant={user.emailConfirmed ? "success" : "planned"}>
              {translate(user.emailConfirmed ? "Email confirmed" : "Email unconfirmed")}
            </Badge>
            <UserStatusBadge status={user.status} />
          </div>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={translate("Display name")}>
              <input
                className={inputClassName}
                name="userDisplayName"
                onChange={(event) => setDisplayName(event.target.value)}
                value={displayName}
              />
            </Field>
            <div className="flex items-end">
              <Button
                className="w-full"
                disabled={pending !== "" || user.status === archivedStatus}
                onClick={() =>
                  void lifecycle(
                    `update-user-${user.id}`,
                    () => updateUser(csrfToken, user, displayName),
                    "User profile updated.",
                  )
                }
                type="button"
                variant="outline"
              >
                <Pencil className="size-4" /> {translate("Save profile")}</Button>
            </div>
          </div>

          <div data-ui-action="set-user-roles">
            <RoleChecks onChange={setRoles} roles={roles} />
            <Button
              className="mt-3"
              disabled={pending !== "" || user.status === archivedStatus}
              onClick={() =>
                void lifecycle(
                  `roles-user-${user.id}`,
                  () => setUserRoles(csrfToken, user, roles),
                  "Passport roles updated.",
                )
              }
              size="sm"
              type="button"
              variant="outline"
            >
              <ShieldCheck className="size-3.5" /> {translate("Save roles")}</Button>
          </div>

          <div className="grid gap-3 border-t border-white/8 pt-4 sm:grid-cols-[1fr_auto]" data-ui-action="reset-user-password">
            <Field label={translate("New password")}>
              <input
                autoComplete="new-password"
                className={inputClassName}
                minLength={12}
                onChange={(event) => setNewPassword(event.target.value)}
                placeholder={translate("At least 12 characters")}
                type="password"
                value={newPassword}
              />
            </Field>
            <div className="flex items-end">
              <Button
                disabled={pending !== "" || newPassword.length < 12 || user.status === archivedStatus}
                onClick={async () => {
                  const updated = await runMutation(
                    `reset-password-${user.id}`,
                    () => resetUserPassword(csrfToken, user, newPassword),
                    [...refresh, reloadSessions],
                    "Password reset and sessions revoked.",
                  );
                  if (updated) setNewPassword("");
                }}
                type="button"
                variant="outline"
              >
                <KeyRound className="size-4" /> {translate("Reset password")}
              </Button>
            </div>
          </div>

          <div className="flex flex-wrap gap-2 border-t border-white/8 pt-4">
            {user.status === pendingStatus && (
              <Button
                data-ui-action="resend-user-invitation"
                disabled={pending !== ""}
                onClick={async () => {
                  const invitation = await runMutation(
                    `resend-user-${user.id}`,
                    () => resendInvitation(csrfToken, user),
                    refresh,
                    "Invitation regenerated.",
                  );
                  if (invitation) {
                    onReveal({
                      expiresAt: invitation.expiresAt,
                      label: `Invitation for ${invitation.user.email}`,
                      value: invitation.invitationUrl,
                    });
                  }
                }}
                size="sm"
                type="button"
                variant="outline"
              >
                <RefreshCcw className="size-3.5" /> {translate("Resend invite")}</Button>
            )}
            {user.status === activeStatus && (
              <Button
                data-ui-action="suspend-user"
                disabled={pending !== ""}
                onClick={() => {
                  if (!window.confirm(translate(`Suspend ${user.email} and revoke all sessions?`))) return;
                  void lifecycle(
                    `suspend-user-${user.id}`,
                    () => suspendUser(csrfToken, user),
                    "User suspended and sessions revoked.",
                  );
                }}
                size="sm"
                type="button"
                variant="outline"
              >
                <Ban className="size-3.5" /> {translate("Suspend")}</Button>
            )}
            {user.status === suspendedStatus && (
              <Button
                data-ui-action="reactivate-user"
                disabled={pending !== ""}
                onClick={() =>
                  void lifecycle(
                    `reactivate-user-${user.id}`,
                    () => reactivateUser(csrfToken, user),
                    "User reactivated.",
                  )
                }
                size="sm"
                type="button"
                variant="outline"
              >
                <Check className="size-3.5" /> {translate("Reactivate")}</Button>
            )}
            {user.status !== archivedStatus ? (
              <Button
                data-ui-action="archive-user"
                disabled={pending !== ""}
                onClick={() => {
                  if (!window.confirm(translate(`Archive ${user.email}?`))) return;
                  void lifecycle(
                    `archive-user-${user.id}`,
                    () => archiveUser(csrfToken, user),
                    "User archived.",
                  );
                }}
                size="sm"
                type="button"
                variant="ghost"
              >
                <Archive className="size-3.5" /> {translate("Archive")}</Button>
            ) : (
              <Button
                data-ui-action="restore-user"
                disabled={pending !== ""}
                onClick={() =>
                  void lifecycle(
                    `restore-user-${user.id}`,
                    () => restoreUser(csrfToken, user),
                    "User restored.",
                  )
                }
                size="sm"
                type="button"
                variant="outline"
              >
                <RotateCcw className="size-3.5" /> {translate("Restore")}</Button>
            )}
          </div>
          <p className="break-all font-mono text-[10px] text-slate-600">
            {user.id} {" "}{translate("· version")}{" "}{user.version} {" "}{translate("· updated")}{" "}{formatTime(user.updatedAt)}
          </p>
        </CardContent>
      </Card>

      <Card data-ui-action="list-user-sessions">
        <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{translate("OIDC sessions")}</CardTitle>
            <CardDescription>{translate("Durable grants and consent records for this subject.")}</CardDescription>
          </div>
          <Button
            data-ui-action="revoke-all-user-sessions"
            disabled={pending !== "" || sessions.every((session) => session.status !== validSession)}
            onClick={() => {
              if (!window.confirm(translate("Revoke every active session for this user?"))) return;
              void runMutation(
                `revoke-all-sessions-${user.id}`,
                () => revokeAllUserSessions(csrfToken, user.id),
                [reloadSessions],
                "All active sessions revoked.",
              );
            }}
            size="sm"
            type="button"
            variant="outline"
          >
            <Ban className="size-3.5" /> {translate("Revoke all")}</Button>
        </CardHeader>
        <CardContent className="space-y-2">
          {sessions.map((session) => (
            <div className="rounded-xl border border-white/8 bg-white/[0.02] p-3" key={session.id}>
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-slate-200">
                    {session.clientDisplayName || session.clientId || "Unknown client"}
                  </p>
                  <p className="mt-1 text-xs text-slate-500">
                    {formatTime(session.createdAt)} · {session.scopes.join(", ") || "no scopes"}
                  </p>
                </div>
                {session.status === validSession ? (
                  <Button
                    data-ui-action="revoke-user-session"
                    disabled={pending !== ""}
                    onClick={() =>
                      void runMutation(
                        `revoke-session-${session.id}`,
                        () => revokeUserSession(csrfToken, user.id, session.id),
                        [reloadSessions],
                        "Session revoked.",
                      )
                    }
                    size="sm"
                    type="button"
                    variant="ghost"
                  >
                    {translate("Revoke")}</Button>
                ) : (
                  <Badge variant="planned">{translate("Revoked")}</Badge>
                )}
              </div>
              <p className="mt-2 break-all font-mono text-[10px] text-slate-700">{session.id}</p>
            </div>
          ))}
          {sessions.length === 0 && <EmptyState text={translate("No OIDC sessions for this user.")} />}
        </CardContent>
      </Card>
    </div>
  );
}

function MembershipsPanel({
  applicationId,
  csrfToken,
  error,
  includeRemoved,
  memberships,
  onApplicationIdChange,
  onIncludeRemovedChange,
  onTenantIdChange,
  onUserIdChange,
  pending,
  reloadMemberships,
  runMutation,
  tenantId,
  userId,
}: {
  applicationId: string;
  csrfToken: string;
  error: unknown;
  includeRemoved: boolean;
  memberships: ApplicationMembershipRecord[];
  onApplicationIdChange: (value: string) => void;
  onIncludeRemovedChange: (value: boolean) => void;
  onTenantIdChange: (value: string) => void;
  onUserIdChange: (value: string) => void;
  pending: string;
  reloadMemberships: () => Promise<unknown>;
  runMutation: MutationRunner;
  tenantId: string;
  userId: string;
}) {
  const [setUserId, setSetUserId] = useState("");
  const [setTenantId, setSetTenantId] = useState("");
  const [setApplicationId, setSetApplicationId] = useState("");
  const [expectedVersion, setExpectedVersion] = useState(0);
  const users = useSWR("identity-membership-users", () =>
    listUsers({ includeArchived: false, pageSize: 100, query: "" }),
  );
  const tenants = useSWR("identity-membership-tenants", () => listTenants(platformPage));
  const setApplications = useSWR(
    setTenantId ? ["identity-membership-applications", setTenantId] : null,
    () => listApplications(setTenantId, platformPage),
  );
  const filterApplications = useSWR(
    tenantId ? ["identity-membership-applications", tenantId] : null,
    () => listApplications(tenantId, platformPage),
  );

  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.8fr_1.2fr]">
      <Card data-ui-action="set-application-membership">
        <CardHeader>
          <CardTitle>{translate("Add application membership")}</CardTitle>
          <CardDescription>
            {translate("Attach an existing global account to one Platform application. Use version 0 for a new membership.")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form
            className="space-y-3"
            onSubmit={(event) => {
              event.preventDefault();
              void runMutation(
                "set-application-membership",
                () =>
                  setApplicationMembership(csrfToken, {
                    applicationId: setApplicationId,
                    expectedVersion,
                    tenantId: setTenantId,
                    userId: setUserId,
                  }),
                [reloadMemberships],
                "Application membership saved.",
              );
            }}
          >
            <SearchableSelect
              ariaLabel={translate("User")}
              className={inputClassName}
              emptyLabel={translate("Select user")}
              label={translate("User")}
              labelClassName={labelClassName}
              onChange={setSetUserId}
              options={(users.data?.users ?? []).map((user) => ({
                label: `${user.displayName} (${user.email})`,
                value: user.id,
              }))}
              required
              value={setUserId}
            />
            <SearchableSelect
              ariaLabel={translate("Tenant")}
              className={inputClassName}
              emptyLabel={translate("Choose a tenant")}
              label={translate("Tenant")}
              labelClassName={labelClassName}
              onChange={(tenantId) => {
                setSetTenantId(tenantId);
                setSetApplicationId("");
              }}
              options={(tenants.data?.tenants ?? []).map((tenant) => ({
                label: `${tenant.displayName} (${tenant.slug})`,
                value: tenant.id,
              }))}
              required
              value={setTenantId}
            />
            <SearchableSelect
              ariaLabel={translate("Application")}
              className={inputClassName}
              disabled={!setTenantId}
              emptyLabel={translate("Choose an application")}
              label={translate("Application")}
              labelClassName={labelClassName}
              onChange={setSetApplicationId}
              options={(setApplications.data?.applications ?? []).map((application) => ({
                label: `${application.displayName} (${application.slug})`,
                value: application.id,
              }))}
              required
              value={setApplicationId}
            />
            <Field label={translate("Expected version") }>
              <input
                className={inputClassName}
                min={0}
                onChange={(event) => setExpectedVersion(Number(event.target.value))}
                type="number"
                value={expectedVersion}
              />
            </Field>
            <div className="flex justify-end">
              <Button disabled={pending !== ""} type="submit">
                <Plus className="size-4" /> {translate("Save membership")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card data-ui-action="list-application-memberships">
        <CardHeader>
          <CardTitle>{translate("Application memberships")}</CardTitle>
          <CardDescription>
            {translate("Filter global users by tenant or application and remove or restore access independently.")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 md:grid-cols-3">
            <SearchableSelect
              ariaLabel={translate("User")}
              className={inputClassName}
              emptyLabel={translate("All users")}
              label={translate("User")}
              labelClassName={labelClassName}
              onChange={onUserIdChange}
              options={(users.data?.users ?? []).map((user) => ({
                label: `${user.displayName} (${user.email})`,
                value: user.id,
              }))}
              value={userId}
            />
            <SearchableSelect
              ariaLabel={translate("Tenant")}
              className={inputClassName}
              emptyLabel={translate("All tenants")}
              label={translate("Tenant")}
              labelClassName={labelClassName}
              onChange={onTenantIdChange}
              options={(tenants.data?.tenants ?? []).map((tenant) => ({
                label: `${tenant.displayName} (${tenant.slug})`,
                value: tenant.id,
              }))}
              value={tenantId}
            />
            <SearchableSelect
              ariaLabel={translate("Application")}
              className={inputClassName}
              disabled={!tenantId}
              emptyLabel={translate("All applications")}
              label={translate("Application")}
              labelClassName={labelClassName}
              onChange={onApplicationIdChange}
              options={(filterApplications.data?.applications ?? []).map((application) => ({
                label: `${application.displayName} (${application.slug})`,
                value: application.id,
              }))}
              value={applicationId}
            />
          </div>
          <label className="mt-3 flex items-center gap-2 text-xs text-slate-500">
            <input
              checked={includeRemoved}
              className="accent-cyan-400"
              onChange={(event) => onIncludeRemovedChange(event.target.checked)}
              type="checkbox"
            />
            {translate("Include removed memberships")}
          </label>
          <ResourceError
            error={
              error ??
              users.error ??
              tenants.error ??
              setApplications.error ??
              filterApplications.error
            }
          />
          <div className="mt-4 space-y-2">
            {memberships.map((membership) => (
              <div
                className="rounded-xl border border-white/8 bg-white/[0.02] p-3"
                data-testid={`application-membership-${membership.applicationId}-${membership.userId}`}
                key={`${membership.applicationId}-${membership.userId}`}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0 space-y-1 font-mono text-[10px] text-slate-500">
                    <p><span className="text-slate-700">user</span> {membership.userId}</p>
                    <p><span className="text-slate-700">tenant</span> {membership.tenantId}</p>
                    <p><span className="text-slate-700">application</span> {membership.applicationId}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant={membership.status === removedMembership ? "planned" : "success"}>
                      {translate(membership.status === removedMembership ? "Removed" : "Active")}
                    </Badge>
                    {membership.status === removedMembership ? (
                      <Button
                        disabled={pending !== ""}
                        onClick={() =>
                          void runMutation(
                            `restore-membership-${membership.applicationId}-${membership.userId}`,
                            () =>
                              setApplicationMembership(csrfToken, {
                                applicationId: membership.applicationId,
                                expectedVersion: membership.version,
                                tenantId: membership.tenantId,
                                userId: membership.userId,
                              }),
                            [reloadMemberships],
                            "Application membership restored.",
                          )
                        }
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        <RotateCcw className="size-3.5" /> {translate("Restore")}
                      </Button>
                    ) : (
                      <Button
                        data-ui-action="remove-application-membership"
                        disabled={pending !== ""}
                        onClick={() => {
                          if (!window.confirm(translate(`Remove membership ${membership.userId}?`))) return;
                          void runMutation(
                            `remove-membership-${membership.applicationId}-${membership.userId}`,
                            () => removeApplicationMembership(csrfToken, membership),
                            [reloadMemberships],
                            "Application membership removed.",
                          );
                        }}
                        size="sm"
                        type="button"
                        variant="ghost"
                      >
                        <Trash2 className="size-3.5" /> {translate("Remove")}
                      </Button>
                    )}
                  </div>
                </div>
                <p className="mt-2 text-[10px] text-slate-700">
                  {translate("Version")} {membership.version} · {formatTime(membership.updatedAt)}
                </p>
              </div>
            ))}
            {memberships.length === 0 && (
              <EmptyState text={translate("No application memberships match this view.")} />
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function ClientsPanel({
  clients,
  csrfToken,
  error,
  onQueryChange,
  onReveal,
  onSelectClient,
  pending,
  query,
  reloadClient,
  reloadClients,
  runMutation,
  scopeNames,
  selectedClient,
}: {
  clients: OidcClientRecord[];
  csrfToken: string;
  error: unknown;
  onQueryChange: (value: string) => void;
  onReveal: (reveal: Reveal) => void;
  onSelectClient: (id: string) => void;
  pending: string;
  query: string;
  reloadClient: () => Promise<unknown>;
  reloadClients: () => Promise<unknown>;
  runMutation: MutationRunner;
  scopeNames: string[];
  selectedClient?: OidcClientRecord;
}) {
  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.9fr_1.1fr]">
      <div className="space-y-5">
        <CreateClientCard
          csrfToken={csrfToken}
          onReveal={onReveal}
          pending={pending}
          reloadClients={reloadClients}
          runMutation={runMutation}
          scopeNames={scopeNames}
        />
        <Card data-ui-action="list-clients">
          <CardHeader>
            <CardTitle>{translate("Registered clients")}</CardTitle>
            <CardDescription>{translate("Public desktop/browser apps and confidential services.")}</CardDescription>
          </CardHeader>
          <CardContent>
            <SearchInput onChange={onQueryChange} placeholder={translate("Client ID or name")} value={query} />
            <ResourceError error={error} />
            <div className="mt-4 space-y-2">
              {clients.map((client) => (
                <button
                  className="flex w-full items-center gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-3 text-left transition hover:border-cyan-300/20 hover:bg-cyan-300/[0.04]"
                  data-ui-action="get-client"
                  key={client.id}
                  onClick={() => onSelectClient(client.clientId)}
                  type="button"
                >
                  <KeyRound className="size-4 shrink-0 text-cyan-300" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-slate-200">{client.displayName}</p>
                    <p className="truncate font-mono text-[10px] text-slate-600">{client.clientId}</p>
                  </div>
                  <div className="flex shrink-0 gap-1.5">
                    {client.isSystem && (
                      <Badge variant="planned">{translate("System resource")}</Badge>
                    )}
                    <Badge variant="planned">
                      {translate(client.applicationType === nativeApplication ? "Native" : "Web")}
                    </Badge>
                    <Badge variant={client.clientType === confidentialClient ? "info" : "planned"}>
                      {translate(client.clientType === confidentialClient ? "Confidential" : "Public")}
                    </Badge>
                  </div>
                </button>
              ))}
              {clients.length === 0 && <EmptyState text={translate("No clients match this view.")} />}
            </div>
          </CardContent>
        </Card>
      </div>
      {selectedClient ? (
        <ClientInspector
          client={selectedClient}
          csrfToken={csrfToken}
          key={`${selectedClient.id}-${selectedClient.version}`}
          onReveal={onReveal}
          pending={pending}
          reloadClient={reloadClient}
          reloadClients={reloadClients}
          runMutation={runMutation}
          scopeNames={scopeNames}
        />
      ) : (
        <SelectionPlaceholder text={translate("Select a client to call the detail API and edit its grants.")} />
      )}
    </div>
  );
}

function CreateClientCard({
  csrfToken,
  onReveal,
  pending,
  reloadClients,
  runMutation,
  scopeNames,
}: {
  csrfToken: string;
  onReveal: (reveal: Reveal) => void;
  pending: string;
  reloadClients: () => Promise<unknown>;
  runMutation: MutationRunner;
  scopeNames: string[];
}) {
  const [clientId, setClientId] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [applicationType, setApplicationType] =
    useState<OidcApplicationType>(webApplication);
  const [clientType, setClientType] = useState<OidcClientType>(publicClient);
  const [grants, setGrants] = useState<OidcGrantType[]>([authorizationCode, refreshToken]);
  const [redirects, setRedirects] = useState("http://localhost/callback");
  const [postLogoutRedirects, setPostLogoutRedirects] = useState("");
  const [scopes, setScopes] = useState("openid, profile, email, roles, asterloom.api");
  const [tenantId, setTenantId] = useState("");
  const [applicationId, setApplicationId] = useState("");
  const [allowUserRegistration, setAllowUserRegistration] = useState(false);
  const [allowMembershipAutoJoin, setAllowMembershipAutoJoin] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const credential = await runMutation(
      "create-client",
      () =>
        createClient(csrfToken, {
          allowMembershipAutoJoin,
          allowUserRegistration,
          applicationId,
          applicationType,
          clientId,
          clientType,
          displayName,
          grantTypes: grants,
          postLogoutRedirectUris: parseLines(postLogoutRedirects),
          redirectUris: parseLines(redirects),
          scopes: parseCsv(scopes),
          tenantId,
        }),
      [reloadClients],
      "OIDC client created.",
    );
    if (credential?.clientSecret) {
      onReveal({ label: `Secret for ${credential.client.clientId}`, value: credential.clientSecret });
    }
  }

  return (
    <Card data-ui-action="create-client">
      <CardHeader>
        <CardTitle>{translate("Register OIDC client")}</CardTitle>
        <CardDescription>{translate("Confidential secrets are generated server-side and shown once.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-3" onSubmit={submit}>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={translate("Client ID")}>
              <input className={inputClassName} onChange={(e) => setClientId(e.target.value)} required value={clientId} />
            </Field>
            <Field label={translate("Display name")}>
              <input className={inputClassName} onChange={(e) => setDisplayName(e.target.value)} required value={displayName} />
            </Field>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={translate("Application type")}>
              <select
                className={inputClassName}
                onChange={(event) => {
                  const next = event.target.value as OidcApplicationType;
                  setApplicationType(next);
                  if (next === nativeApplication) {
                    setClientType(publicClient);
                    setGrants([authorizationCode, refreshToken]);
                    setRedirects("http://localhost/");
                    setAllowUserRegistration(false);
                  }
                }}
                value={applicationType}
              >
                <option value={webApplication}>{translate("Web / service")}</option>
                <option value={nativeApplication}>{translate("Native desktop / mobile")}</option>
              </select>
            </Field>
            <Field label={translate("Client type")}>
              <select
                className={inputClassName}
                disabled={applicationType === nativeApplication}
                onChange={(event) => {
                  const next = event.target.value as OidcClientType;
                  setClientType(next);
                  if (next === publicClient) {
                    setGrants((current) =>
                      current.filter(
                        (grant) => grant !== clientCredentials,
                      ),
                    );
                    setAllowUserRegistration(false);
                  }
                }}
                value={clientType}
              >
                <option value={publicClient}>{translate("Public (PKCE)")}</option>
                <option value={confidentialClient}>{translate("Confidential")}</option>
              </select>
            </Field>
          </div>
          <GrantChecks
            grants={grants}
            onChange={(next) => {
              setGrants(next);
              if (next.includes(clientCredentials)) setClientType(confidentialClient);
              if (!next.includes(clientCredentials)) setAllowUserRegistration(false);
            }}
          />
          <ClientBindingFields
            allowMembershipAutoJoin={allowMembershipAutoJoin}
            allowUserRegistration={allowUserRegistration}
            applicationId={applicationId}
            onAllowMembershipAutoJoinChange={setAllowMembershipAutoJoin}
            onAllowUserRegistrationChange={setAllowUserRegistration}
            onApplicationIdChange={setApplicationId}
            onTenantIdChange={setTenantId}
            registrationAvailable={
              clientType === confidentialClient && grants.includes(clientCredentials)
            }
            tenantId={tenantId}
          />
          <ClientTextFields
            onPostLogoutRedirectsChange={setPostLogoutRedirects}
            onRedirectsChange={setRedirects}
            onScopesChange={setScopes}
            postLogoutRedirects={postLogoutRedirects}
            redirects={redirects}
            scopeNames={scopeNames}
            scopes={scopes}
          />
          <div className="flex justify-end">
            <Button disabled={pending !== ""} type="submit">
              <Plus className="size-4" /> {translate("Register client")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function ClientInspector({
  client,
  csrfToken,
  onReveal,
  pending,
  reloadClient,
  reloadClients,
  runMutation,
  scopeNames,
}: {
  client: OidcClientRecord;
  csrfToken: string;
  onReveal: (reveal: Reveal) => void;
  pending: string;
  reloadClient: () => Promise<unknown>;
  reloadClients: () => Promise<unknown>;
  runMutation: MutationRunner;
  scopeNames: string[];
}) {
  const [displayName, setDisplayName] = useState(client.displayName);
  const [grants, setGrants] = useState<OidcGrantType[]>(client.grantTypes);
  const [redirects, setRedirects] = useState(client.redirectUris.join("\n"));
  const [postLogoutRedirects, setPostLogoutRedirects] = useState(
    client.postLogoutRedirectUris.join("\n"),
  );
  const [scopes, setScopes] = useState(client.scopes.join(", "));
  const [tenantId, setTenantId] = useState(client.tenantId);
  const [applicationId, setApplicationId] = useState(client.applicationId);
  const [allowUserRegistration, setAllowUserRegistration] = useState(
    client.allowUserRegistration,
  );
  const [allowMembershipAutoJoin, setAllowMembershipAutoJoin] = useState(
    client.allowMembershipAutoJoin,
  );
  const refresh = [reloadClients, reloadClient];

  return (
    <Card data-ui-action="update-client">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>{client.displayName}</CardTitle>
          <CardDescription>{client.clientId}</CardDescription>
        </div>
        <div className="flex gap-1.5">
          {client.isSystem && (
            <Badge variant="planned">{translate("System resource")}</Badge>
          )}
          <Badge variant="planned">
            {translate(client.applicationType === nativeApplication ? "Native" : "Web")}
          </Badge>
          <Badge variant={client.clientType === confidentialClient ? "info" : "planned"}>
            {translate(client.clientType === confidentialClient ? "Confidential" : "Public")}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <fieldset className="space-y-4" disabled={!client.isMutable}>
          <Field label={translate("Display name")}>
            <input className={inputClassName} onChange={(e) => setDisplayName(e.target.value)} value={displayName} />
          </Field>
          <GrantChecks
            grants={grants}
            onChange={(next) => {
              const allowed =
                client.clientType === publicClient
                  ? next.filter(
                      (grant) => grant !== clientCredentials,
                    )
                  : next;
              setGrants(allowed);
              if (!allowed.includes(clientCredentials)) setAllowUserRegistration(false);
            }}
          />
          <ClientBindingFields
            allowMembershipAutoJoin={allowMembershipAutoJoin}
            allowUserRegistration={allowUserRegistration}
            applicationId={applicationId}
            onAllowMembershipAutoJoinChange={setAllowMembershipAutoJoin}
            onAllowUserRegistrationChange={setAllowUserRegistration}
            onApplicationIdChange={setApplicationId}
            onTenantIdChange={setTenantId}
            registrationAvailable={
              client.clientType === confidentialClient && grants.includes(clientCredentials)
            }
            tenantId={tenantId}
          />
          <ClientTextFields
            onPostLogoutRedirectsChange={setPostLogoutRedirects}
            onRedirectsChange={setRedirects}
            onScopesChange={setScopes}
            postLogoutRedirects={postLogoutRedirects}
            redirects={redirects}
            scopeNames={scopeNames}
            scopes={scopes}
          />
        </fieldset>
        {client.isMutable ? (
          <div className="flex flex-wrap justify-end gap-2 border-t border-white/8 pt-4">
          {client.clientType === confidentialClient && (
            <Button
              data-ui-action="rotate-client-secret"
              disabled={pending !== ""}
              onClick={async () => {
                if (!window.confirm(translate(`Rotate the secret for ${client.clientId}?`))) return;
                const credential = await runMutation(
                  `rotate-client-${client.id}`,
                  () => rotateClientSecret(csrfToken, client),
                  refresh,
                  "Client secret rotated.",
                );
                if (credential?.clientSecret) {
                  onReveal({ label: `New secret for ${client.clientId}`, value: credential.clientSecret });
                }
              }}
              type="button"
              variant="outline"
            >
              <RefreshCcw className="size-4" /> {translate("Rotate secret")}</Button>
          )}
          <Button
            disabled={pending !== ""}
            onClick={() =>
              void runMutation(
                `update-client-${client.id}`,
                () =>
                  updateClient(csrfToken, client, {
                    allowMembershipAutoJoin,
                    allowUserRegistration,
                    applicationId,
                    displayName,
                    grantTypes: grants,
                    postLogoutRedirectUris: parseLines(postLogoutRedirects),
                    redirectUris: parseLines(redirects),
                    scopes: parseCsv(scopes),
                    tenantId,
                  }),
                refresh,
                "OIDC client updated.",
              )
            }
            type="button"
          >
            <Pencil className="size-4" /> {translate("Save client")}</Button>
          <Button
            data-ui-action="delete-client"
            disabled={pending !== ""}
            onClick={() => {
              if (!window.confirm(translate(`Permanently delete ${client.clientId}?`))) return;
              void runMutation(
                `delete-client-${client.id}`,
                () => deleteClient(csrfToken, client),
                [reloadClients],
                "OIDC client deleted.",
              );
            }}
            type="button"
            variant="ghost"
          >
            <Trash2 className="size-4" /> {translate("Delete")}</Button>
          </div>
        ) : (
          <div
            className="flex gap-3 rounded-xl border border-cyan-300/15 bg-cyan-300/[0.045] p-3 text-xs leading-5 text-slate-500"
            data-testid="identity-system-resource-notice"
          >
            <ShieldCheck className="mt-0.5 size-4 shrink-0 text-cyan-300" />
            <span>{translate("This system resource is managed by deployment configuration and cannot be changed or deleted here.")}</span>
          </div>
        )}
        <p className="break-all font-mono text-[10px] text-slate-600">
          {client.id} {" "}{translate("· version")}{" "}{client.version}
        </p>
      </CardContent>
    </Card>
  );
}

function ScopesPanel({
  csrfToken,
  error,
  onQueryChange,
  onSelectScope,
  pending,
  query,
  reloadScope,
  reloadScopes,
  runMutation,
  scopes,
  selectedScope,
}: {
  csrfToken: string;
  error: unknown;
  onQueryChange: (value: string) => void;
  onSelectScope: (id: string) => void;
  pending: string;
  query: string;
  reloadScope: () => Promise<unknown>;
  reloadScopes: () => Promise<unknown>;
  runMutation: MutationRunner;
  scopes: OidcScopeRecord[];
  selectedScope?: OidcScopeRecord;
}) {
  return (
    <div className="grid items-start gap-5 xl:grid-cols-[0.9fr_1.1fr]">
      <div className="space-y-5">
        <CreateScopeCard csrfToken={csrfToken} pending={pending} reloadScopes={reloadScopes} runMutation={runMutation} />
        <Card data-ui-action="list-scopes">
          <CardHeader>
            <CardTitle>{translate("OIDC scopes")}</CardTitle>
            <CardDescription>{translate("Named access boundaries and their target API resources.")}</CardDescription>
          </CardHeader>
          <CardContent>
            <SearchInput onChange={onQueryChange} placeholder={translate("Scope name")} value={query} />
            <ResourceError error={error} />
            <div className="mt-4 space-y-2">
              {scopes.map((scope) => (
                <button
                  className="flex w-full items-center gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-3 text-left transition hover:border-cyan-300/20 hover:bg-cyan-300/[0.04]"
                  data-ui-action="get-scope"
                  key={scope.id}
                  onClick={() => onSelectScope(scope.id)}
                  type="button"
                >
                  <ShieldCheck className="size-4 shrink-0 text-cyan-300" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-mono text-xs text-slate-200">{scope.name}</p>
                    <p className="truncate text-xs text-slate-500">{scope.displayName}</p>
                  </div>
                  <div className="flex shrink-0 gap-1.5">
                    {scope.isSystem && (
                      <Badge variant="planned">{translate("System resource")}</Badge>
                    )}
                    <Badge variant="planned">{scope.resources.length} {" "}{translate("resources")}</Badge>
                  </div>
                </button>
              ))}
              {scopes.length === 0 && <EmptyState text={translate("No scopes match this view.")} />}
            </div>
          </CardContent>
        </Card>
      </div>
      {selectedScope ? (
        <ScopeInspector
          csrfToken={csrfToken}
          key={`${selectedScope.id}-${selectedScope.version}`}
          pending={pending}
          reloadScope={reloadScope}
          reloadScopes={reloadScopes}
          runMutation={runMutation}
          scope={selectedScope}
        />
      ) : (
        <SelectionPlaceholder text={translate("Select a scope to load its authoritative detail.")} />
      )}
    </div>
  );
}

function CreateScopeCard({
  csrfToken,
  pending,
  reloadScopes,
  runMutation,
}: {
  csrfToken: string;
  pending: string;
  reloadScopes: () => Promise<unknown>;
  runMutation: MutationRunner;
}) {
  const [name, setName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [resources, setResources] = useState("");
  return (
    <Card data-ui-action="create-scope">
      <CardHeader>
        <CardTitle>{translate("Create API scope")}</CardTitle>
        <CardDescription>{translate("Scopes can be assigned to clients after creation.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="space-y-3"
          onSubmit={(event) => {
            event.preventDefault();
            void runMutation(
              "create-scope",
              () => createScope(csrfToken, { description, displayName, name, resources: parseCsv(resources) }),
              [reloadScopes],
              "OIDC scope created.",
            );
          }}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={translate("Scope name")}>
              <input className={inputClassName} onChange={(e) => setName(e.target.value)} required value={name} />
            </Field>
            <Field label={translate("Display name")}>
              <input className={inputClassName} onChange={(e) => setDisplayName(e.target.value)} required value={displayName} />
            </Field>
          </div>
          <Field label={translate("Description")}>
            <input className={inputClassName} onChange={(e) => setDescription(e.target.value)} value={description} />
          </Field>
          <Field label={translate("Resources (comma separated)")}>
            <input className={inputClassName} onChange={(e) => setResources(e.target.value)} placeholder={translate("my-api, my-worker")} value={resources} />
          </Field>
          <div className="flex justify-end">
            <Button disabled={pending !== ""} type="submit"><Plus className="size-4" /> {" "}{translate("Create scope")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function ScopeInspector({
  csrfToken,
  pending,
  reloadScope,
  reloadScopes,
  runMutation,
  scope,
}: {
  csrfToken: string;
  pending: string;
  reloadScope: () => Promise<unknown>;
  reloadScopes: () => Promise<unknown>;
  runMutation: MutationRunner;
  scope: OidcScopeRecord;
}) {
  const [displayName, setDisplayName] = useState(scope.displayName);
  const [description, setDescription] = useState(scope.description);
  const [resources, setResources] = useState(scope.resources.join(", "));
  const refresh = [reloadScopes, reloadScope];
  return (
    <Card data-ui-action="update-scope">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>{scope.displayName}</CardTitle>
          <CardDescription>{scope.name}</CardDescription>
        </div>
        {scope.isSystem && (
          <Badge variant="planned">{translate("System resource")}</Badge>
        )}
      </CardHeader>
      <CardContent className="space-y-4">
        <fieldset className="space-y-4" disabled={!scope.isMutable}>
          <Field label={translate("Display name")}>
            <input className={inputClassName} onChange={(e) => setDisplayName(e.target.value)} value={displayName} />
          </Field>
          <Field label={translate("Description")}>
            <textarea className={textAreaClassName} onChange={(e) => setDescription(e.target.value)} value={description} />
          </Field>
          <Field label={translate("Resources (comma separated)")}>
            <input className={inputClassName} onChange={(e) => setResources(e.target.value)} value={resources} />
          </Field>
        </fieldset>
        {scope.isMutable ? (
          <div className="flex flex-wrap justify-end gap-2 border-t border-white/8 pt-4">
          <Button
            disabled={pending !== ""}
            onClick={() =>
              void runMutation(
                `update-scope-${scope.id}`,
                () => updateScope(csrfToken, scope, { description, displayName, resources: parseCsv(resources) }),
                refresh,
                "OIDC scope updated.",
              )
            }
            type="button"
          ><Pencil className="size-4" /> {" "}{translate("Save scope")}</Button>
          <Button
            data-ui-action="delete-scope"
            disabled={pending !== ""}
            onClick={() => {
              if (!window.confirm(translate(`Delete scope ${scope.name}?`))) return;
              void runMutation(
                `delete-scope-${scope.id}`,
                () => deleteScope(csrfToken, scope),
                [reloadScopes],
                "OIDC scope deleted.",
              );
            }}
            type="button"
            variant="ghost"
          ><Trash2 className="size-4" /> {" "}{translate("Delete")}</Button>
          </div>
        ) : (
          <div
            className="flex gap-3 rounded-xl border border-cyan-300/15 bg-cyan-300/[0.045] p-3 text-xs leading-5 text-slate-500"
            data-testid="identity-system-resource-notice"
          >
            <ShieldCheck className="mt-0.5 size-4 shrink-0 text-cyan-300" />
            <span>{translate("This system resource is managed by deployment configuration and cannot be changed or deleted here.")}</span>
          </div>
        )}
        <p className="break-all font-mono text-[10px] text-slate-600">{scope.id} {" "}{translate("· version")}{" "}{scope.version}</p>
      </CardContent>
    </Card>
  );
}

function RoleChecks({
  onChange,
  roles,
}: {
  onChange: (roles: PassportRole[]) => void;
  roles: PassportRole[];
}) {
  return (
    <fieldset className="rounded-xl border border-white/8 p-3">
      <legend className="px-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-600">
        {translate("Trusted Passport roles")}</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {passportRoles.map((role) => (
          <label className="flex items-center gap-2 text-xs text-slate-400" key={role}>
            <input
              checked={roles.includes(role)}
              className="accent-cyan-400"
              onChange={(event) =>
                onChange(
                  event.target.checked
                    ? [...roles, role]
                    : roles.filter((candidate) => candidate !== role),
                )
              }
              type="checkbox"
            />
            {role}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function GrantChecks({
  grants,
  onChange,
}: {
  grants: OidcGrantType[];
  onChange: (grants: OidcGrantType[]) => void;
}) {
  const options: Array<[OidcGrantType, string]> = [
    [authorizationCode, "Authorization code + PKCE"],
    [clientCredentials, "Client credentials"],
    [refreshToken, "Refresh token"],
  ];
  return (
    <fieldset className="rounded-xl border border-white/8 p-3">
      <legend className="px-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-600">{translate("Grant types")}</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {options.map(([value, label]) => (
          <label className="flex items-center gap-2 text-xs text-slate-400" key={value}>
            <input
              checked={grants.includes(value)}
              className="accent-cyan-400"
              onChange={(event) =>
                onChange(event.target.checked ? [...grants, value] : grants.filter((item) => item !== value))
              }
              type="checkbox"
            />
            {translate(label)}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function ClientBindingFields({
  allowMembershipAutoJoin,
  allowUserRegistration,
  applicationId,
  onAllowMembershipAutoJoinChange,
  onAllowUserRegistrationChange,
  onApplicationIdChange,
  onTenantIdChange,
  registrationAvailable,
  tenantId,
}: {
  allowMembershipAutoJoin: boolean;
  allowUserRegistration: boolean;
  applicationId: string;
  onAllowMembershipAutoJoinChange: (value: boolean) => void;
  onAllowUserRegistrationChange: (value: boolean) => void;
  onApplicationIdChange: (value: string) => void;
  onTenantIdChange: (value: string) => void;
  registrationAvailable: boolean;
  tenantId: string;
}) {
  const tenants = useSWR("identity-client-binding-tenants", () => listTenants(platformPage));
  const applications = useSWR(
    tenantId ? ["identity-client-binding-applications", tenantId] : null,
    () => listApplications(tenantId, platformPage),
  );

  return (
    <fieldset className="rounded-xl border border-white/8 p-3">
      <legend className="px-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-600">
        {translate("Platform application binding")}
      </legend>
      <p className="mb-3 text-xs leading-5 text-slate-600">
        {translate("Bound clients issue application-scoped tokens and enforce application membership.")}
      </p>
      <div className="grid gap-3 sm:grid-cols-2">
        <SearchableSelect
          ariaLabel={translate("Tenant")}
          className={inputClassName}
          emptyLabel={translate("None")}
          label={translate("Tenant (optional)")}
          labelClassName={labelClassName}
          onChange={(tenantId) => {
            onTenantIdChange(tenantId);
            onApplicationIdChange("");
          }}
          options={(tenants.data?.tenants ?? []).map((tenant) => ({
            label: `${tenant.displayName} (${tenant.slug})`,
            value: tenant.id,
          }))}
          value={tenantId}
        />
        <SearchableSelect
          ariaLabel={translate("Application")}
          className={inputClassName}
          disabled={!tenantId}
          emptyLabel={translate("None")}
          label={translate("Application (optional)")}
          labelClassName={labelClassName}
          onChange={onApplicationIdChange}
          options={(applications.data?.applications ?? []).map((application) => ({
            label: `${application.displayName} (${application.slug})`,
            value: application.id,
          }))}
          value={applicationId}
        />
      </div>
      <ResourceError error={tenants.error ?? applications.error} />
      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        <label className="flex items-center gap-2 text-xs text-slate-400">
          <input
            checked={allowMembershipAutoJoin}
            className="accent-cyan-400"
            onChange={(event) => onAllowMembershipAutoJoinChange(event.target.checked)}
            type="checkbox"
          />
          {translate("Auto-join existing accounts on login")}
        </label>
        <label className="flex items-center gap-2 text-xs text-slate-400">
          <input
            checked={allowUserRegistration}
            className="accent-cyan-400"
            disabled={!registrationAvailable}
            onChange={(event) => onAllowUserRegistrationChange(event.target.checked)}
            type="checkbox"
          />
          {translate("Allow trusted backend registration")}
        </label>
      </div>
    </fieldset>
  );
}

function ClientTextFields({
  onPostLogoutRedirectsChange,
  onRedirectsChange,
  onScopesChange,
  postLogoutRedirects,
  redirects,
  scopeNames,
  scopes,
}: {
  onPostLogoutRedirectsChange: (value: string) => void;
  onRedirectsChange: (value: string) => void;
  onScopesChange: (value: string) => void;
  postLogoutRedirects: string;
  redirects: string;
  scopeNames: string[];
  scopes: string;
}) {
  return (
    <>
      <Field label={translate("Redirect URIs (one per line)")}>
        <textarea className={textAreaClassName} onChange={(e) => onRedirectsChange(e.target.value)} value={redirects} />
      </Field>
      <Field label={translate("Post-logout redirect URIs (one per line)")}>
        <textarea className={textAreaClassName} onChange={(e) => onPostLogoutRedirectsChange(e.target.value)} value={postLogoutRedirects} />
      </Field>
      <SearchableMultiSelect
        ariaLabel={translate("Add scope")}
        className={inputClassName}
        emptyLabel={translate("Select a scope")}
        label={translate("Scopes")}
        labelClassName={labelClassName}
        onChange={(value) => onScopesChange(value.join(", "))}
        options={scopeNames.map((name) => ({ label: name, value: name }))}
        value={parseCsv(scopes)}
      />
    </>
  );
}

function CredentialReveal({ onClose, reveal }: { onClose: () => void; reveal: Reveal }) {
  async function copy() {
    await navigator.clipboard.writeText(reveal.value);
    toast.success(translate("Copied to clipboard."));
  }
  return (
    <Card className="border-amber-300/20 bg-amber-300/[0.055]" data-testid="identity-credential-reveal">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>{translate(reveal.label)}</CardTitle>
          <CardDescription>
            {translate("This value is returned once. Copy it now and store it in an approved secret channel.")} {translate(reveal.expiresAt ? `Expires ${formatTime(reveal.expiresAt)}.` : "")}
          </CardDescription>
        </div>
        <Button onClick={onClose} size="sm" type="button" variant="ghost">{translate("Dismiss")}</Button>
      </CardHeader>
      <CardContent>
        <div className="flex items-start gap-2 rounded-xl border border-amber-300/15 bg-slate-950/70 p-3">
          <code className="min-w-0 flex-1 break-all text-xs leading-5 text-amber-200">{reveal.value}</code>
          <Button aria-label={translate("Copy credential")} onClick={() => void copy()} size="icon" type="button" variant="outline">
            <Clipboard className="size-4" />
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function UserStatusBadge({ status }: { status: IdentityUserRecord["status"] }) {
  const label = status.split("_").at(-1)?.toLowerCase() ?? "unknown";
  return <Badge variant={status === activeStatus ? "success" : status === pendingStatus ? "info" : "planned"}>{label}</Badge>;
}

function SearchInput({ onChange, placeholder, value }: { onChange: (value: string) => void; placeholder: string; value: string }) {
  return (
    <label className="relative block min-w-0 flex-1">
      <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-600" />
      <input className={cn(inputClassName, "pl-9")} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} type="search" value={value} />
    </label>
  );
}

function Field({ children, label }: { children: ReactNode; label: string }) {
  return <label><span className={labelClassName}>{label}</span>{children}</label>;
}

function ResourceError({ error }: { error: unknown }) {
  if (!error) return null;
  return (
    <div className="mt-4 flex items-start gap-2 rounded-xl border border-rose-400/15 bg-rose-400/[0.06] p-3 text-xs text-rose-300">
      <CircleAlert className="mt-0.5 size-4 shrink-0" /> {translate(identityErrorMessage(error))}
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return <div className="rounded-xl border border-dashed border-white/10 p-7 text-center text-sm text-slate-600">{text}</div>;
}

function SelectionPlaceholder({ text }: { text: string }) {
  return (
    <Card className="grid min-h-72 place-items-center border-dashed">
      <div className="max-w-sm p-8 text-center">
        <Eye className="mx-auto size-6 text-slate-700" />
        <p className="mt-3 text-sm leading-6 text-slate-500">{text}</p>
      </div>
    </Card>
  );
}

function parseCsv(value: string): string[] {
  return value.split(",").map((item) => item.trim()).filter(Boolean);
}

function parseLines(value: string): string[] {
  return value.split(/\r?\n/).map((item) => item.trim()).filter(Boolean);
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
