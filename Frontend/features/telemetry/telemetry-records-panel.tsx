"use client";

import { Activity, BarChart3, ChevronLeft, ChevronRight, ScrollText, Search } from "lucide-react";
import { type FormEvent, useState } from "react";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { formatDateTime } from "@/lib/i18n/format";
import { translate } from "@/lib/i18n/locale";
import {
  listTelemetryRecords,
  type TelemetryRecord,
  type TelemetryScope,
  type TelemetrySignalType,
} from "@/lib/api/telemetry-management";
import { TelemetrySignalTypeObject } from "@/lib/api/generated/models";
import { cn } from "@/lib/utils/cn";

import {
  TelemetryEmpty,
  TelemetryError,
  TelemetryLoading,
  telemetryInputClassName,
  telemetryLabelClassName,
} from "./telemetry-ui";

const signals = [
  { icon: Activity, label: "Traces", value: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE },
  { icon: BarChart3, label: "Metrics", value: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_METRIC },
  { icon: ScrollText, label: "Logs", value: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_LOG },
] as const;

type Filters = {
  fromAt?: string;
  query: string;
  serviceName: string;
  toAt?: string;
  traceId: string;
};

const emptyFilters: Filters = { query: "", serviceName: "", traceId: "" };

export function TelemetryRecordsPanel({ scope }: { scope: TelemetryScope }) {
  const [signalType, setSignalType] = useState<TelemetrySignalType>(
    TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
  );
  const [serviceDraft, setServiceDraft] = useState("");
  const [traceDraft, setTraceDraft] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [fromDraft, setFromDraft] = useState("");
  const [toDraft, setToDraft] = useState("");
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [pageTokens, setPageTokens] = useState([""]);
  const [pageIndex, setPageIndex] = useState(0);
  const [selected, setSelected] = useState<TelemetryRecord | null>(null);
  const pageToken = pageTokens[pageIndex] ?? "";
  const records = useSWR(
    ["telemetry-records", scope.tenantId, scope.applicationId, scope.environmentId, signalType, filters, pageToken],
    () => listTelemetryRecords(scope, { ...filters, pageSize: 50, pageToken, signalType }),
    { dedupingInterval: 0 },
  );

  function selectSignal(value: TelemetrySignalType) {
    setSignalType(value);
    setPageTokens([""]);
    setPageIndex(0);
    setSelected(null);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    setFilters({
      fromAt: toIso(fromDraft),
      query: queryDraft.trim(),
      serviceName: serviceDraft.trim(),
      toAt: toIso(toDraft),
      traceId: traceDraft.trim().toLowerCase(),
    });
    setPageTokens([""]);
    setPageIndex(0);
    setSelected(null);
  }

  function nextPage() {
    const next = records.data?.nextPageToken;
    if (!next) return;
    setPageTokens((current) => [...current.slice(0, pageIndex + 1), next]);
    setPageIndex((current) => current + 1);
    setSelected(null);
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>{translate("Stored telemetry")}</CardTitle>
          <CardDescription>{translate("Query traces, metric points, and logs stored in PostgreSQL for this environment.")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div aria-label={translate("Telemetry signal type")} className="flex flex-wrap gap-2" role="group">
            {signals.map(({ icon: Icon, label, value }) => (
              <Button
                aria-pressed={signalType === value}
                key={value}
                onClick={() => selectSignal(value)}
                type="button"
                variant={signalType === value ? "default" : "outline"}
              >
                <Icon aria-hidden="true" className="size-4" /> {translate(label)}
              </Button>
            ))}
          </div>
          <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-5" onSubmit={submit}>
            <label className={telemetryLabelClassName}>{translate("Service name")}<input className={telemetryInputClassName} name="telemetryRecordServiceName" onChange={(event) => setServiceDraft(event.target.value)} value={serviceDraft} /></label>
            <label className={telemetryLabelClassName}>{translate("Trace ID")}<input className={telemetryInputClassName} name="telemetryRecordTraceId" onChange={(event) => setTraceDraft(event.target.value)} placeholder={translate("32 hexadecimal characters")} value={traceDraft} /></label>
            <label className={telemetryLabelClassName}>{translate("Search")}<input className={telemetryInputClassName} name="telemetryRecordQuery" onChange={(event) => setQueryDraft(event.target.value)} placeholder={translate("Name, value, or attribute")} value={queryDraft} /></label>
            <label className={telemetryLabelClassName}>{translate("From")}<input className={telemetryInputClassName} name="telemetryRecordFrom" onChange={(event) => setFromDraft(event.target.value)} type="datetime-local" value={fromDraft} /></label>
            <label className={telemetryLabelClassName}>{translate("To")}<input className={telemetryInputClassName} name="telemetryRecordTo" onChange={(event) => setToDraft(event.target.value)} type="datetime-local" value={toDraft} /></label>
            <div className="md:col-span-2 xl:col-span-5"><Button type="submit" variant="outline"><Search className="size-4" /> {translate("Query telemetry")}</Button></div>
          </form>
        </CardContent>
      </Card>

      <Card data-ui-action="list-telemetry-records">
        <CardHeader className="flex-row items-start justify-between gap-4">
          <div><CardTitle>{translate("Signal records")}</CardTitle><CardDescription>{translate("Select a record to inspect its normalized attributes and original OTLP payload.")}</CardDescription></div>
          <Button aria-label={translate("Refresh telemetry records")} onClick={() => void records.mutate()} size="sm" type="button" variant="outline">{translate("Refresh")}</Button>
        </CardHeader>
        <CardContent className="space-y-4">
          {records.isLoading ? <TelemetryLoading label={translate("Loading telemetry records")} />
            : records.error ? <TelemetryError error={records.error} />
            : (records.data?.records.length ?? 0) === 0 ? <TelemetryEmpty message={translate("No telemetry records match these filters.")} />
            : <div className="space-y-2">{records.data?.records.map((record) => (
                <button
                  className={cn("grid w-full gap-3 rounded-xl border p-4 text-left transition md:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_auto]", selected?.id === record.id ? "border-violet-400/35 bg-violet-400/[0.08]" : "border-white/8 bg-white/[0.02] hover:bg-white/[0.04]")}
                  key={record.id}
                  onClick={() => setSelected(record)}
                  type="button"
                >
                  <div className="min-w-0"><p className="truncate text-sm font-medium text-slate-100">{record.name}</p><p className="mt-1 truncate font-mono text-xs text-slate-500">{record.serviceName}</p></div>
                  <div className="min-w-0"><p className="truncate text-xs text-slate-400">{record.value || record.traceId || "—"}</p><p className="mt-1 truncate font-mono text-[11px] text-violet-300">{record.traceId}</p></div>
                  <div className="text-right"><Badge variant="info">{record.category || translate("Unspecified")}</Badge><p className="mt-2 text-xs text-slate-600">{formatDateTime(record.observedAt)}</p>{record.durationMilliseconds !== null && <p className="mt-1 text-xs text-slate-500">{record.durationMilliseconds.toFixed(2)} ms</p>}</div>
                </button>
              ))}</div>}
          <div className="flex items-center justify-between">
            <Button disabled={pageIndex === 0} onClick={() => { setPageIndex((current) => current - 1); setSelected(null); }} type="button" variant="outline"><ChevronLeft className="size-4" /> {translate("Previous")}</Button>
            <span className="text-xs text-slate-600">{translate("Page")} {pageIndex + 1}</span>
            <Button disabled={!records.data?.nextPageToken} onClick={nextPage} type="button" variant="outline">{translate("Next")} <ChevronRight className="size-4" /></Button>
          </div>
        </CardContent>
      </Card>

      {selected && <Card>
        <CardHeader><CardTitle>{translate("Record details")}</CardTitle><CardDescription className="break-all font-mono">{selected.id}</CardDescription></CardHeader>
        <CardContent className="grid gap-4 lg:grid-cols-2">
          <JsonBlock label={translate("Attributes")} value={selected.attributesJson} />
          <JsonBlock label={translate("OTLP payload")} value={selected.payloadJson} />
        </CardContent>
      </Card>}
    </div>
  );
}

function JsonBlock({ label, value }: { label: string; value: string }) {
  return <section><h3 className="mb-2 text-xs font-medium text-slate-400">{label}</h3><pre className="max-h-96 overflow-auto rounded-xl border border-white/8 bg-slate-950/80 p-4 text-xs leading-5 text-slate-300">{prettyJson(value)}</pre></section>;
}

function prettyJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function toIso(value: string) {
  return value ? new Date(value).toISOString() : undefined;
}
