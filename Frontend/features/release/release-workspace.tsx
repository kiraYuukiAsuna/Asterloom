"use client";

import { KeyRound, PackageCheck, Radio, RefreshCw } from "lucide-react";
import Link from "next/link";
import { useMemo } from "react";
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
  listApplications,
  listEnvironments,
  listTenants,
} from "@/lib/api/platform-management";
import type { ReleaseScope } from "@/lib/api/release-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { ReleaseArtifactsPanel } from "./release-artifacts-panel";
import { ReleaseChannelsPanel } from "./release-channels-panel";
import { ReleaseReleasesPanel } from "./release-releases-panel";
import { useReleaseSelection } from "./release-store";
import {
  releaseInputClassName,
  releaseLabelClassName,
  ReleaseEmptyState,
  ReleaseErrorState,
} from "./release-ui";
import { translate } from "@/lib/i18n/locale";

const page = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function ReleaseWorkspace({
  csrfToken,
  view,
}: {
  csrfToken: string;
  view: "artifacts" | "channels" | "releases";
}) {
  const hydrated = useHydrated();
  const selection = useReleaseSelection();
  const tenants = useSWR(hydrated ? "release-scope-tenants" : null, () =>
    listTenants(page),
  );
  const applications = useSWR(
    selection.tenantId
      ? ["release-scope-applications", selection.tenantId]
      : null,
    () => listApplications(selection.tenantId, page),
  );
  const environments = useSWR(
    selection.tenantId && selection.applicationId
      ? [
          "release-scope-environments",
          selection.tenantId,
          selection.applicationId,
        ]
      : null,
    () =>
      listEnvironments(selection.tenantId, selection.applicationId, page),
  );
  const scope = useMemo<ReleaseScope | null>(
    () =>
      selection.tenantId && selection.applicationId && selection.environmentId
        ? {
            applicationId: selection.applicationId,
            environmentId: selection.environmentId,
            tenantId: selection.tenantId,
          }
        : null,
    [selection.applicationId, selection.environmentId, selection.tenantId],
  );
  const scopeError = tenants.error ?? applications.error ?? environments.error;

  return (
    <div
      className="space-y-6"
      data-hydrated={hydrated ? "true" : "false"}
      data-release-workspace
    >
      <section className="theme-hero-violet flex flex-col gap-5 rounded-2xl border border-violet-400/15 bg-gradient-to-br from-violet-400/[0.09] via-slate-950/60 to-sky-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">{translate("Signed desktop delivery")}</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">
            {translate("Release control center")}</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            {translate("Verify signed artifacts, publish immutable manifests, and control targeted, deterministic desktop rollouts without transferring private keys.")}</p>
        </div>
        <nav
          aria-label={translate("Release views")}
          className="flex flex-wrap rounded-xl border border-white/10 p-1"
        >
          <ReleaseTab
            active={view === "releases"}
            href="/releases"
            icon={PackageCheck}
            label={translate("Releases")}
          />
          <ReleaseTab
            active={view === "channels"}
            href="/channels"
            icon={Radio}
            label={translate("Channels")}
          />
          <ReleaseTab
            active={view === "artifacts"}
            href="/artifacts"
            icon={KeyRound}
            label={translate("Artifacts & keys")}
          />
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{translate("Release boundary")}</CardTitle>
            <CardDescription>
              {translate("Channels, artifacts, and manifests are isolated by tenant, application, and environment.")}</CardDescription>
          </div>
          <Button
            aria-label={translate("Refresh release scope")}
            onClick={() => {
              void tenants.mutate();
              void applications.mutate();
              void environments.mutate();
            }}
            size="sm"
            type="button"
            variant="outline"
          >
            <RefreshCw aria-hidden="true" className="size-3.5" />
            {translate("Refresh")}</Button>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-3">
          <label className={releaseLabelClassName}>
            {translate("Release tenant")}<select
              aria-label={translate("Release tenant")}
              className={releaseInputClassName}
              onChange={(event) => selection.selectTenant(event.target.value)}
              value={selection.tenantId}
            >
              <option value="">{translate("Choose a tenant")}</option>
              {(tenants.data?.tenants ?? []).map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.displayName} ({tenant.slug})
                </option>
              ))}
            </select>
          </label>
          <label className={releaseLabelClassName}>
            {translate("Release application")}<select
              aria-label={translate("Release application")}
              className={releaseInputClassName}
              disabled={!selection.tenantId}
              onChange={(event) => selection.selectApplication(event.target.value)}
              value={selection.applicationId}
            >
              <option value="">{translate("Choose an application")}</option>
              {(applications.data?.applications ?? []).map((application) => (
                <option key={application.id} value={application.id}>
                  {application.displayName} ({application.slug})
                </option>
              ))}
            </select>
          </label>
          <label className={releaseLabelClassName}>
            {translate("Release environment")}<select
              aria-label={translate("Release environment")}
              className={releaseInputClassName}
              disabled={!selection.applicationId}
              onChange={(event) => selection.selectEnvironment(event.target.value)}
              value={selection.environmentId}
            >
              <option value="">{translate("Choose an environment")}</option>
              {(environments.data?.environments ?? []).map((environment) => (
                <option key={environment.id} value={environment.id}>
                  {environment.displayName} ({environment.slug})
                </option>
              ))}
            </select>
          </label>
          {scopeError && (
            <div className="md:col-span-3">
              <ReleaseErrorState error={scopeError} />
            </div>
          )}
        </CardContent>
      </Card>

      {!scope ? (
        <ReleaseEmptyState message={translate("Choose a tenant, application, and environment to manage desktop delivery.")} />
      ) : view === "channels" ? (
        <ReleaseChannelsPanel csrfToken={csrfToken} scope={scope} />
      ) : view === "artifacts" ? (
        <ReleaseArtifactsPanel csrfToken={csrfToken} scope={scope} />
      ) : (
        <ReleaseReleasesPanel csrfToken={csrfToken} scope={scope} />
      )}
    </div>
  );
}

function ReleaseTab({
  active,
  href,
  icon: Icon,
  label,
}: {
  active: boolean;
  href: string;
  icon: typeof PackageCheck;
  label: string;
}) {
  return (
    <Link
      className={cn(
        "flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-medium transition",
        active
          ? "bg-violet-400/15 text-violet-100"
          : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
      )}
      href={href}
    >
      <Icon aria-hidden="true" className="size-3.5" />
      {label}
    </Link>
  );
}
