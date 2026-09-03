"use client";

import { Activity, Database, RadioTower, RefreshCw } from "lucide-react";
import Link from "next/link";
import { useMemo } from "react";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { listApplications, listEnvironments, listTenants } from "@/lib/api/platform-management";
import type { TelemetryScope } from "@/lib/api/telemetry-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { TelemetryHealthPanel } from "./telemetry-health-panel";
import { TelemetryRecordsPanel } from "./telemetry-records-panel";
import { TelemetrySourcesPanel } from "./telemetry-sources-panel";
import { useTelemetrySelection } from "./telemetry-store";
import { TelemetryEmpty, TelemetryError, telemetryInputClassName, telemetryLabelClassName } from "./telemetry-ui";
import { translate } from "@/lib/i18n/locale";

const page = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function TelemetryWorkspace({ csrfToken, view }: { csrfToken: string; view: "health" | "signals" | "sources" }) {
  const hydrated = useHydrated();
  const selection = useTelemetrySelection();
  const tenants = useSWR(hydrated ? "telemetry-scope-tenants" : null, () => listTenants(page));
  const applications = useSWR(
    selection.tenantId ? ["telemetry-scope-applications", selection.tenantId] : null,
    () => listApplications(selection.tenantId, page),
  );
  const environments = useSWR(
    selection.tenantId && selection.applicationId
      ? ["telemetry-scope-environments", selection.tenantId, selection.applicationId]
      : null,
    () => listEnvironments(selection.tenantId, selection.applicationId, page),
  );
  const scope = useMemo<TelemetryScope | null>(
    () => selection.tenantId && selection.applicationId && selection.environmentId
      ? { tenantId: selection.tenantId, applicationId: selection.applicationId, environmentId: selection.environmentId }
      : null,
    [selection.applicationId, selection.environmentId, selection.tenantId],
  );
  const scopeError = tenants.error ?? applications.error ?? environments.error;

  return (
    <div className="space-y-6" data-hydrated={hydrated ? "true" : "false"} data-telemetry-workspace>
      <section className="theme-hero-violet flex flex-col gap-5 rounded-2xl border border-violet-400/15 bg-gradient-to-br from-violet-400/[0.09] via-slate-950/60 to-cyan-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">{translate("Technical observability")}</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">{translate("Telemetry control center")}</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            {translate("Register services, store OTLP signals in PostgreSQL, and query technical telemetry by environment.")}</p>
        </div>
        <nav aria-label={translate("Telemetry views")} className="flex rounded-xl border border-white/10 p-1">
          <TelemetryTab active={view === "health"} href="/telemetry/health" icon={Activity} label={translate("Health & errors")} />
          <TelemetryTab active={view === "signals"} href="/telemetry/signals" icon={Database} label={translate("Stored signals")} />
          <TelemetryTab active={view === "sources"} href="/telemetry/sources" icon={RadioTower} label={translate("Sources & storage")} />
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{translate("Telemetry boundary")}</CardTitle>
            <CardDescription>{translate("Source identity and policy are isolated per tenant, application, and environment.")}</CardDescription>
          </div>
          <Button aria-label={translate("Refresh telemetry scope")} onClick={() => { void tenants.mutate(); void applications.mutate(); void environments.mutate(); }} size="sm" type="button" variant="outline">
            <RefreshCw aria-hidden="true" className="size-3.5" /> {translate("Refresh")}</Button>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-3">
          <SearchableSelect ariaLabel={translate("Telemetry tenant")} className={telemetryInputClassName} emptyLabel={translate("Choose a tenant")} label={translate("Tenant")} labelClassName={telemetryLabelClassName} onChange={selection.selectTenant} options={(tenants.data?.tenants ?? []).map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))} value={selection.tenantId} />
          <SearchableSelect ariaLabel={translate("Telemetry application")} className={telemetryInputClassName} disabled={!selection.tenantId} emptyLabel={translate("Choose an application")} label={translate("Application")} labelClassName={telemetryLabelClassName} onChange={selection.selectApplication} options={(applications.data?.applications ?? []).map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))} value={selection.applicationId} />
          <SearchableSelect ariaLabel={translate("Telemetry environment")} className={telemetryInputClassName} disabled={!selection.applicationId} emptyLabel={translate("Choose an environment")} label={translate("Environment")} labelClassName={telemetryLabelClassName} onChange={selection.selectEnvironment} options={(environments.data?.environments ?? []).map((environment) => ({ label: `${environment.displayName} (${environment.slug})`, value: environment.id }))} value={selection.environmentId} />
          {scopeError && <div className="md:col-span-3"><TelemetryError error={scopeError} /></div>}
        </CardContent>
      </Card>

      {!scope ? <TelemetryEmpty message={translate("Choose a tenant, application, and environment to manage telemetry.")} />
        : view === "sources" ? <TelemetrySourcesPanel csrfToken={csrfToken} scope={scope} />
        : view === "signals" ? <TelemetryRecordsPanel scope={scope} />
        : <TelemetryHealthPanel csrfToken={csrfToken} scope={scope} />}
    </div>
  );
}

function TelemetryTab({ active, href, icon: Icon, label }: { active: boolean; href: string; icon: typeof Activity; label: string }) {
  return (
    <Link className={cn("flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-medium transition", active ? "bg-violet-400/15 text-violet-100" : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200")} href={href}>
      <Icon aria-hidden="true" className="size-3.5" />{label}
    </Link>
  );
}
