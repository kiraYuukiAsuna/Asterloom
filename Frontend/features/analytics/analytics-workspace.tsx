"use client";

import { ChartNoAxesCombined, DatabaseZap, RefreshCw } from "lucide-react";
import Link from "next/link";
import { useMemo } from "react";
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
import type { AnalyticsScope } from "@/lib/api/analytics-management";
import { listApplications, listEnvironments, listTenants } from "@/lib/api/platform-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { AnalyticsExplorerPanel } from "./analytics-explorer-panel";
import { AnalyticsSchemasPanel } from "./analytics-schemas-panel";
import { useAnalyticsSelection } from "./analytics-store";
import {
  AnalyticsEmpty,
  AnalyticsError,
  analyticsInputClassName,
  analyticsLabelClassName,
} from "./analytics-ui";
import { translate } from "@/lib/i18n/locale";

const page = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };

export function AnalyticsWorkspace({
  csrfToken,
  view,
}: {
  csrfToken: string;
  view: "explorer" | "schemas";
}) {
  const hydrated = useHydrated();
  const selection = useAnalyticsSelection();
  const tenants = useSWR(hydrated ? "analytics-scope-tenants" : null, () => listTenants(page));
  const applications = useSWR(
    selection.tenantId ? ["analytics-scope-applications", selection.tenantId] : null,
    () => listApplications(selection.tenantId, page),
  );
  const environments = useSWR(
    selection.tenantId && selection.applicationId
      ? ["analytics-scope-environments", selection.tenantId, selection.applicationId]
      : null,
    () => listEnvironments(selection.tenantId, selection.applicationId, page),
  );
  const scope = useMemo<AnalyticsScope | null>(
    () =>
      selection.tenantId && selection.applicationId && selection.environmentId
        ? {
            tenantId: selection.tenantId,
            applicationId: selection.applicationId,
            environmentId: selection.environmentId,
          }
        : null,
    [selection.applicationId, selection.environmentId, selection.tenantId],
  );
  const scopeError = tenants.error ?? applications.error ?? environments.error;

  return (
    <div className="space-y-6" data-analytics-workspace data-hydrated={hydrated ? "true" : "false"}>
      <section className="theme-hero-cyan flex flex-col gap-5 rounded-2xl border border-cyan-400/15 bg-gradient-to-br from-cyan-400/[0.09] via-slate-950/60 to-violet-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">{translate("Product intelligence")}</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">
            {translate("Analytics control center")}</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            {translate("Govern event contracts and write keys, inspect redacted payloads, and query product outcomes without mixing technical telemetry into business events.")}</p>
        </div>
        <nav aria-label={translate("Analytics views")} className="flex rounded-xl border border-white/10 p-1">
          <AnalyticsTab active={view === "explorer"} href="/analytics/explorer" icon={ChartNoAxesCombined} label={translate("Explorer")} />
          <AnalyticsTab active={view === "schemas"} href="/analytics/schemas" icon={DatabaseZap} label={translate("Schemas & keys")} />
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{translate("Analytics boundary")}</CardTitle>
            <CardDescription>
              {translate("Schemas, write keys, retention, and events are isolated per environment.")}</CardDescription>
          </div>
          <Button
            aria-label={translate("Refresh analytics scope")}
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
          <SearchableSelect
            ariaLabel={translate("Analytics tenant")}
            className={analyticsInputClassName}
            emptyLabel={translate("Choose a tenant")}
            label={translate("Tenant")}
            labelClassName={analyticsLabelClassName}
            onChange={selection.selectTenant}
            options={(tenants.data?.tenants ?? []).map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))}
            value={selection.tenantId}
          />
          <SearchableSelect
            ariaLabel={translate("Analytics application")}
            className={analyticsInputClassName}
            disabled={!selection.tenantId}
            emptyLabel={translate("Choose an application")}
            label={translate("Application")}
            labelClassName={analyticsLabelClassName}
            onChange={selection.selectApplication}
            options={(applications.data?.applications ?? []).map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))}
            value={selection.applicationId}
          />
          <SearchableSelect
            ariaLabel={translate("Analytics environment")}
            className={analyticsInputClassName}
            disabled={!selection.applicationId}
            emptyLabel={translate("Choose an environment")}
            label={translate("Environment")}
            labelClassName={analyticsLabelClassName}
            onChange={selection.selectEnvironment}
            options={(environments.data?.environments ?? []).map((environment) => ({ label: `${environment.displayName} (${environment.slug})`, value: environment.id }))}
            value={selection.environmentId}
          />
          {scopeError && <div className="md:col-span-3"><AnalyticsError error={scopeError} /></div>}
        </CardContent>
      </Card>

      {!scope ? (
        <AnalyticsEmpty message={translate("Choose a tenant, application, and environment to manage analytics.")} />
      ) : view === "schemas" ? (
        <AnalyticsSchemasPanel csrfToken={csrfToken} scope={scope} />
      ) : (
        <AnalyticsExplorerPanel csrfToken={csrfToken} scope={scope} />
      )}
    </div>
  );
}

function AnalyticsTab({
  active,
  href,
  icon: Icon,
  label,
}: {
  active: boolean;
  href: string;
  icon: typeof ChartNoAxesCombined;
  label: string;
}) {
  return (
    <Link
      className={cn(
        "flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-medium transition",
        active ? "bg-cyan-400/15 text-cyan-100" : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
      )}
      href={href}
    >
      <Icon aria-hidden="true" className="size-3.5" />
      {label}
    </Link>
  );
}
