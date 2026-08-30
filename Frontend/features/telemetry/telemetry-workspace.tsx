"use client";

import { Activity, RadioTower, RefreshCw } from "lucide-react";
import Link from "next/link";
import { useMemo } from "react";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { listApplications, listEnvironments, listTenants } from "@/lib/api/platform-management";
import type { TelemetryScope } from "@/lib/api/telemetry-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { TelemetryHealthPanel } from "./telemetry-health-panel";
import { TelemetrySourcesPanel } from "./telemetry-sources-panel";
import { useTelemetrySelection } from "./telemetry-store";
import { TelemetryEmpty, TelemetryError, telemetryInputClassName, telemetryLabelClassName } from "./telemetry-ui";

const page = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function TelemetryWorkspace({ csrfToken, view }: { csrfToken: string; view: "health" | "sources" }) {
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
      <section className="flex flex-col gap-5 rounded-2xl border border-violet-400/15 bg-gradient-to-br from-violet-400/[0.09] via-slate-950/60 to-cyan-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">Technical observability</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">Telemetry control center</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            Register services, govern OTLP sampling and export, and pivot from recent failures into the configured observability backend.
          </p>
        </div>
        <nav aria-label="Telemetry views" className="flex rounded-xl border border-white/10 p-1">
          <TelemetryTab active={view === "health"} href="/telemetry/health" icon={Activity} label="Health & errors" />
          <TelemetryTab active={view === "sources"} href="/telemetry/sources" icon={RadioTower} label="Sources & export" />
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>Telemetry boundary</CardTitle>
            <CardDescription>Source identity and policy are isolated per tenant, application, and environment.</CardDescription>
          </div>
          <Button aria-label="Refresh telemetry scope" onClick={() => { void tenants.mutate(); void applications.mutate(); void environments.mutate(); }} size="sm" type="button" variant="outline">
            <RefreshCw aria-hidden="true" className="size-3.5" /> Refresh
          </Button>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-3">
          <label className={telemetryLabelClassName}>Tenant
            <select aria-label="Telemetry tenant" className={telemetryInputClassName} onChange={(event) => selection.selectTenant(event.target.value)} value={selection.tenantId}>
              <option value="">Choose a tenant</option>
              {(tenants.data?.tenants ?? []).map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.displayName} ({tenant.slug})</option>)}
            </select>
          </label>
          <label className={telemetryLabelClassName}>Application
            <select aria-label="Telemetry application" className={telemetryInputClassName} disabled={!selection.tenantId} onChange={(event) => selection.selectApplication(event.target.value)} value={selection.applicationId}>
              <option value="">Choose an application</option>
              {(applications.data?.applications ?? []).map((application) => <option key={application.id} value={application.id}>{application.displayName} ({application.slug})</option>)}
            </select>
          </label>
          <label className={telemetryLabelClassName}>Environment
            <select aria-label="Telemetry environment" className={telemetryInputClassName} disabled={!selection.applicationId} onChange={(event) => selection.selectEnvironment(event.target.value)} value={selection.environmentId}>
              <option value="">Choose an environment</option>
              {(environments.data?.environments ?? []).map((environment) => <option key={environment.id} value={environment.id}>{environment.displayName} ({environment.slug})</option>)}
            </select>
          </label>
          {scopeError && <div className="md:col-span-3"><TelemetryError error={scopeError} /></div>}
        </CardContent>
      </Card>

      {!scope ? <TelemetryEmpty message="Choose a tenant, application, and environment to manage telemetry." />
        : view === "sources" ? <TelemetrySourcesPanel csrfToken={csrfToken} scope={scope} />
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
