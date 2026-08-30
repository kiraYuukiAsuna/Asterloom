"use client";

import { Activity, ExternalLink, LoaderCircle, RefreshCw, Search, TriangleAlert } from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  getTelemetryCollectorHealth,
  getTelemetryDiagnosticLink,
  listTelemetryErrors,
  telemetryErrorMessage,
  type TelemetryDiagnosticLinkRecord,
  type TelemetryScope,
} from "@/lib/api/telemetry-management";

import {
  TelemetryEmpty,
  TelemetryError,
  TelemetryLoading,
  TelemetryStatusBadge,
  telemetryInputClassName,
  telemetryLabelClassName,
} from "./telemetry-ui";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

export function TelemetryHealthPanel({ csrfToken, scope }: { csrfToken: string; scope: TelemetryScope }) {
  const [serviceDraft, setServiceDraft] = useState("");
  const [traceDraft, setTraceDraft] = useState("");
  const [filters, setFilters] = useState({ serviceName: "", traceId: "" });
  const health = useSWR(
    ["telemetry-collector-health", scope.tenantId, scope.applicationId, scope.environmentId],
    () => getTelemetryCollectorHealth(scope),
    { dedupingInterval: 0 },
  );
  const errors = useSWR(
    ["telemetry-errors", scope.tenantId, scope.applicationId, scope.environmentId, filters],
    () => listTelemetryErrors(scope, { ...filters, pageSize: 100 }),
    { dedupingInterval: 0 },
  );

  return (
    <div className="space-y-6">
      <div className="grid gap-6 lg:grid-cols-[0.8fr_1.2fr]">
        <Card data-ui-action="get-telemetry-collector-health">
          <CardHeader className="flex-row items-start justify-between gap-4">
            <div><CardTitle>{translate("Collector health")}</CardTitle><CardDescription>{translate("The health extension is probed independently from platform readiness.")}</CardDescription></div>
            <Button aria-label={translate("Refresh Collector health")} onClick={() => void health.mutate()} size="sm" type="button" variant="outline"><RefreshCw className="size-3.5" /></Button>
          </CardHeader>
          <CardContent>
            {health.isLoading ? <TelemetryLoading label={translate("Checking Collector health")} />
              : health.error ? <TelemetryError error={health.error} />
              : health.data ? <div className="space-y-4" data-testid="telemetry-collector-health">
                  <div className="flex items-center justify-between rounded-xl border border-white/8 bg-white/[0.02] p-4">
                    <div className="flex items-center gap-3"><div className="grid size-10 place-items-center rounded-xl bg-violet-400/10 text-violet-300"><Activity className="size-5" /></div><div><p className="text-sm font-medium text-white">{translate("OpenTelemetry Collector")}</p><p className="mt-1 font-mono text-xs text-slate-500">{health.data.endpoint}</p></div></div>
                    <TelemetryStatusBadge status={health.data.status} />
                  </div>
                  <dl className="grid grid-cols-2 gap-3 text-xs"><div><dt className="text-slate-600">{translate("Latency")}</dt><dd className="mt-1 text-slate-300">{health.data.latencyMilliseconds} {" "}{translate("ms")}</dd></div><div><dt className="text-slate-600">{translate("Checked")}</dt><dd className="mt-1 text-slate-300">{formatDateTime(health.data.checkedAt)}</dd></div></dl>
                  <p className="text-xs leading-5 text-slate-500">{health.data.message}</p>
                </div> : null}
          </CardContent>
        </Card>
        <DiagnosticLinkCard csrfToken={csrfToken} scope={scope} />
      </div>

      <Card data-ui-action="list-telemetry-errors">
        <CardHeader><div className="flex items-center gap-2"><TriangleAlert className="size-4 text-amber-300" /><CardTitle>{translate("Recent technical errors")}</CardTitle></div><CardDescription>{translate("Unhandled RPC failures captured with request, trace, and span correlation. Full logs and traces stay in the observability backend.")}</CardDescription></CardHeader>
        <CardContent className="space-y-4">
          <form className="grid gap-3 md:grid-cols-[1fr_1fr_auto]" onSubmit={(event) => { event.preventDefault(); setFilters({ serviceName: serviceDraft.trim(), traceId: traceDraft.trim().toLowerCase() }); }}>
            <label className={telemetryLabelClassName}>{translate("Service name")}<input className={telemetryInputClassName} name="telemetryErrorServiceName" onChange={(event) => setServiceDraft(event.target.value)} placeholder={translate("Asterloom.Server")} value={serviceDraft} /></label>
            <label className={telemetryLabelClassName}>{translate("Trace ID")}<input className={telemetryInputClassName} name="telemetryErrorTraceId" onChange={(event) => setTraceDraft(event.target.value)} placeholder={translate("32 hexadecimal characters")} value={traceDraft} /></label>
            <Button className="md:mt-[22px]" type="submit" variant="outline"><Search className="size-4" /> {" "}{translate("Filter errors")}</Button>
          </form>
          {errors.isLoading ? <TelemetryLoading label={translate("Loading recent errors")} />
            : errors.error ? <TelemetryError error={errors.error} />
            : (errors.data?.errors.length ?? 0) === 0 ? <TelemetryEmpty message={translate("No recent unhandled errors match this environment and filter.")} />
            : <div className="space-y-3">{errors.data?.errors.map((item) => (
                <article className="rounded-xl border border-white/8 bg-white/[0.02] p-4" data-testid={`telemetry-error-${item.id}`} key={item.id}>
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between"><div><p className="font-medium text-rose-200">{item.exceptionType}</p><p className="mt-1 text-sm text-slate-400">{item.message}</p></div><span className="text-xs text-slate-600">{formatDateTime(item.occurredAt)}</span></div>
                  <dl className="mt-3 grid gap-2 text-xs sm:grid-cols-3"><div><dt className="text-slate-600">{translate("Service")}</dt><dd className="mt-1 font-mono text-slate-400">{item.serviceName}</dd></div><div><dt className="text-slate-600">{translate("Request")}</dt><dd className="mt-1 break-all font-mono text-slate-400">{item.requestId}</dd></div><div><dt className="text-slate-600">{translate("Trace")}</dt><dd className="mt-1 break-all font-mono text-violet-300">{item.traceId || "not sampled"}</dd></div></dl>
                </article>
              ))}</div>}
        </CardContent>
      </Card>
    </div>
  );
}

function DiagnosticLinkCard({ csrfToken, scope }: { csrfToken: string; scope: TelemetryScope }) {
  const [traceId, setTraceId] = useState("");
  const [fromAt, setFromAt] = useState("");
  const [toAt, setToAt] = useState("");
  const [result, setResult] = useState<TelemetryDiagnosticLinkRecord | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setResult(null);
    try {
      setResult(await getTelemetryDiagnosticLink(csrfToken, scope, { traceId, fromAt: toIso(fromAt), toAt: toIso(toAt) }));
      toast.success(translate("Controlled diagnostic link created."));
    } catch (error) { toast.error(translate(telemetryErrorMessage(error))); } finally { setBusy(false); }
  }
  return (
    <Card>
      <CardHeader><CardTitle>{translate("Trace pivot")}</CardTitle><CardDescription>{translate("Create a controlled link to the environment's configured observability backend.")}</CardDescription></CardHeader>
      <CardContent><form className="grid gap-4" onSubmit={submit}>
        <label className={telemetryLabelClassName}>{translate("W3C Trace ID")}<input className={telemetryInputClassName} name="telemetryDiagnosticTraceId" onChange={(event) => setTraceId(event.target.value)} placeholder={translate("32 hexadecimal characters")} value={traceId} /></label>
        <div className="grid gap-3 sm:grid-cols-2"><label className={telemetryLabelClassName}>{translate("From (optional)")}<input className={telemetryInputClassName} name="telemetryDiagnosticFrom" onChange={(event) => setFromAt(event.target.value)} type="datetime-local" value={fromAt} /></label><label className={telemetryLabelClassName}>{translate("To (optional)")}<input className={telemetryInputClassName} name="telemetryDiagnosticTo" onChange={(event) => setToAt(event.target.value)} type="datetime-local" value={toAt} /></label></div>
        <Button data-ui-action="get-telemetry-diagnostic-link" disabled={busy} type="submit">{busy ? <LoaderCircle className="size-4 animate-spin" /> : <ExternalLink className="size-4" />} {" "}{translate("Create diagnostic link")}</Button>
        {result && <a className="flex items-center justify-between gap-3 rounded-xl border border-violet-400/20 bg-violet-400/[0.06] p-4 text-sm text-violet-200 hover:bg-violet-400/[0.1]" data-testid="telemetry-diagnostic-link" href={result.url} rel="noreferrer" target="_blank"><span className="min-w-0 truncate font-mono">{result.traceId}</span><ExternalLink className="size-4 shrink-0" /></a>}
      </form></CardContent>
    </Card>
  );
}

function toIso(value: string) { return value ? new Date(value).toISOString() : undefined; }
