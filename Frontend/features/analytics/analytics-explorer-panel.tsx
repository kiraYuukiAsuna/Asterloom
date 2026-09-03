"use client";

import { BarChart3, Download, Eye, LoaderCircle, Search } from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Button } from "@/components/ui/button";
import { SearchableMultiSelect } from "@/components/ui/searchable-multi-select";
import { SearchableSelect, type SearchableSelectOption } from "@/components/ui/searchable-select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  analyticsErrorMessage,
  exportAnalyticsEvents,
  getAnalyticsEvent,
  listAnalyticsSchemas,
  listAnalyticsEvents,
  queryAnalytics,
  type AnalyticsAggregationBucket,
  type AnalyticsEventRecord,
  type AnalyticsScope,
} from "@/lib/api/analytics-management";
import { cn } from "@/lib/utils/cn";

import {
  AnalyticsEmpty,
  AnalyticsError,
  AnalyticsLoading,
  analyticsInputClassName,
  analyticsLabelClassName,
} from "./analytics-ui";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

type EventFilters = {
  actorId: string;
  eventId: string;
  eventName: string;
  fromAt: string;
  toAt: string;
};

const emptyFilters: EventFilters = {
  actorId: "",
  eventId: "",
  eventName: "",
  fromAt: "",
  toAt: "",
};

export function AnalyticsExplorerPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: AnalyticsScope;
}) {
  const [draft, setDraft] = useState<EventFilters>(emptyFilters);
  const [filters, setFilters] = useState<EventFilters>(emptyFilters);
  const [selected, setSelected] = useState<AnalyticsEventRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const schemas = useSWR(
    ["analytics-event-options", scope.tenantId, scope.applicationId, scope.environmentId],
    () => listAnalyticsSchemas(scope, { includeArchived: true, pageSize: 100 }),
  );
  const eventOptions: SearchableSelectOption[] = (schemas.data?.eventSchemas ?? []).map(
    (schema) => ({ label: `${schema.displayName} (${schema.key})`, value: schema.key }),
  );
  const events = useSWR(
    ["analytics-events", scope.tenantId, scope.applicationId, scope.environmentId, filters],
    () =>
      listAnalyticsEvents(scope, {
        ...filters,
        fromAt: toOptionalIso(filters.fromAt),
        pageSize: 100,
        toAt: toOptionalIso(filters.toAt),
      }),
    { dedupingInterval: 0, keepPreviousData: true, revalidateOnMount: true },
  );

  function applyFilters(event: FormEvent) {
    event.preventDefault();
    setFilters(draft);
    setSelected(null);
  }

  async function inspect(item: AnalyticsEventRecord) {
    setGettingId(item.id);
    try {
      setSelected(await getAnalyticsEvent(scope, item.id));
      toast.success(translate("Analytics event refreshed."));
    } catch (error) {
      toast.error(translate(analyticsErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
        <Card data-ui-action="list-analytics-events">
          <CardHeader>
            <CardTitle>{translate("Event stream")}</CardTitle>
            <CardDescription>
              {translate("Browse accepted, schema-validated event payloads after server-side redaction.")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <form className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" onSubmit={applyFilters}>
              <SearchableSelect ariaLabel={translate("Event name")} className={analyticsInputClassName} emptyLabel={translate("All events")} label={translate("Event name")} labelClassName={analyticsLabelClassName} onChange={(value) => setDraft((current) => ({ ...current, eventName: value }))} options={eventOptions} value={draft.eventName} />
              <FilterInput label={translate("Actor / anonymous ID")} name="analyticsActor" onChange={(value) => setDraft((current) => ({ ...current, actorId: value }))} placeholder={translate("user-123")} value={draft.actorId} />
              <FilterInput label={translate("Event ID")} name="analyticsEventId" onChange={(value) => setDraft((current) => ({ ...current, eventId: value }))} placeholder={translate("SDK idempotency ID")} value={draft.eventId} />
              <FilterInput label={translate("From")} name="analyticsFrom" onChange={(value) => setDraft((current) => ({ ...current, fromAt: value }))} type="datetime-local" value={draft.fromAt} />
              <FilterInput label={translate("To")} name="analyticsTo" onChange={(value) => setDraft((current) => ({ ...current, toAt: value }))} type="datetime-local" value={draft.toAt} />
              <div className="flex items-end gap-2">
                <Button type="submit" variant="outline"><Search className="size-4" /> {" "}{translate("Apply filters")}</Button>
                <Button onClick={() => { setDraft(emptyFilters); setFilters(emptyFilters); }} type="button" variant="ghost">{translate("Clear")}</Button>
              </div>
            </form>

            {events.isLoading ? (
              <AnalyticsLoading label={translate("Loading analytics events")} />
            ) : events.error ? (
              <AnalyticsError error={events.error} />
            ) : (events.data?.events.length ?? 0) === 0 ? (
              <AnalyticsEmpty message={translate("No analytics events match these filters.")} />
            ) : (
              <div className="space-y-2">
                {events.data?.events.map((item) => (
                  <article
                    className={cn(
                      "rounded-xl border p-4",
                      selected?.id === item.id
                        ? "border-cyan-400/30 bg-cyan-400/[0.06]"
                        : "border-white/8 bg-white/[0.02]",
                    )}
                    data-testid={`analytics-event-${item.eventId}`}
                    key={item.id}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <button className="min-w-0 text-left" onClick={() => setSelected(item)} type="button">
                        <p className="font-mono text-sm font-medium text-cyan-200">{item.eventName}</p>
                        <p className="mt-1 truncate text-xs text-slate-500">
                          {item.actorId || item.anonymousId} · {formatDateTime(item.occurredAt)}
                        </p>
                        <p className="mt-1 truncate font-mono text-[11px] text-slate-600">{item.eventId}</p>
                      </button>
                      <Button
                        data-ui-action="get-analytics-event"
                        disabled={gettingId === item.id}
                        onClick={() => void inspect(item)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {gettingId === item.id ? <LoaderCircle className="size-3.5 animate-spin" /> : <Eye className="size-3.5" />}
                        {translate("Inspect")}</Button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {selected ? <EventInspector event={selected} /> : <AnalyticsEmpty message={translate("Select an event to inspect its redacted properties and context.")} />}
      </div>

      <AnalyticsQueryCard csrfToken={csrfToken} eventOptions={eventOptions} scope={scope} />
      <AnalyticsExportCard csrfToken={csrfToken} eventOptions={eventOptions} scope={scope} />
    </div>
  );
}

function EventInspector({ event }: { event: AnalyticsEventRecord }) {
  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <CardTitle className="font-mono text-cyan-200">{event.eventName}</CardTitle>
        <CardDescription>
          <span className="block">{event.eventId}</span>
          <span className="mt-1 block break-all font-mono text-[10px] text-slate-600">{event.id}</span>
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <dl className="grid grid-cols-2 gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-xs">
          <div><dt className="text-slate-600">{translate("Actor")}</dt><dd className="mt-1 break-all text-slate-300">{event.actorId || event.anonymousId}</dd></div>
          <div><dt className="text-slate-600">{translate("Schema")}</dt><dd className="mt-1 text-slate-300">{translate("v")}{event.schemaVersion}</dd></div>
          <div><dt className="text-slate-600">{translate("SDK")}</dt><dd className="mt-1 text-slate-300">{event.sdkName || "unknown"} {event.sdkVersion}</dd></div>
          <div><dt className="text-slate-600">{translate("Write key")}</dt><dd className="mt-1 font-mono text-slate-300">{event.writeKeyPrefix}</dd></div>
        </dl>
        <JsonBlock label={translate("Properties")} value={event.propertiesJson} />
        <JsonBlock label={translate("Context")} value={event.contextJson} />
      </CardContent>
    </Card>
  );
}

function AnalyticsQueryCard({ csrfToken, eventOptions, scope }: { csrfToken: string; eventOptions: SearchableSelectOption[]; scope: AnalyticsScope }) {
  const defaults = defaultRange();
  const [eventNames, setEventNames] = useState("");
  const [fromAt, setFromAt] = useState(defaults.fromAt);
  const [toAt, setToAt] = useState(defaults.toAt);
  const [interval, setInterval] = useState<"hour" | "day" | "week">("day");
  const [buckets, setBuckets] = useState<AnalyticsAggregationBucket[]>([]);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const result = await queryAnalytics(csrfToken, scope, {
        eventNames: eventNames.split(",").map((item) => item.trim()).filter(Boolean),
        fromAt: new Date(fromAt).toISOString(),
        toAt: new Date(toAt).toISOString(),
        interval,
      });
      setBuckets(result.buckets);
      toast.success(translate("Analytics aggregation refreshed."));
    } catch (error) {
      toast.error(translate(analyticsErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  const maximum = Math.max(1, ...buckets.map((bucket) => bucket.eventCount));
  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Outcome query")}</CardTitle>
        <CardDescription>{translate("Aggregate event volume and unique actors by a bounded time interval.")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <form className="grid gap-3 md:grid-cols-4" onSubmit={submit}>
          <SearchableMultiSelect ariaLabel={translate("Add event name")} className={analyticsInputClassName} emptyLabel={translate("Select an event name")} label={translate("Event names")} labelClassName={analyticsLabelClassName} onChange={(value) => setEventNames(value.join(", "))} options={eventOptions} required value={eventNames.split(",").map((item) => item.trim()).filter(Boolean)} />
          <FilterInput label={translate("From")} name="queryFrom" onChange={setFromAt} type="datetime-local" value={fromAt} />
          <FilterInput label={translate("To")} name="queryTo" onChange={setToAt} type="datetime-local" value={toAt} />
          <label className={analyticsLabelClassName}>{translate("Interval")}<select className={analyticsInputClassName} onChange={(event) => setInterval(event.target.value as typeof interval)} value={interval}><option value="hour">{translate("Hour")}</option><option value="day">{translate("Day")}</option><option value="week">{translate("Week")}</option></select></label>
          <div className="md:col-span-4">
            <Button data-ui-action="query-analytics-aggregation" disabled={busy} type="submit">
              {busy ? <LoaderCircle className="size-4 animate-spin" /> : <BarChart3 className="size-4" />}
              {translate("Run query")}</Button>
          </div>
        </form>
        {buckets.length > 0 && (
          <div className="space-y-2" data-testid="analytics-query-results">
            {buckets.map((bucket) => (
              <div className="grid gap-2 rounded-lg border border-white/8 bg-white/[0.02] p-3 sm:grid-cols-[11rem_1fr_8rem] sm:items-center" key={`${bucket.periodStart}:${bucket.eventName}`}>
                <div><p className="font-mono text-xs text-cyan-200">{bucket.eventName}</p><p className="mt-1 text-[11px] text-slate-600">{formatDateTime(bucket.periodStart)}</p></div>
                <div className="h-2 overflow-hidden rounded-full bg-white/[0.05]"><div className="h-full rounded-full bg-cyan-400/60" style={{ width: `${Math.max(2, (bucket.eventCount / maximum) * 100)}%` }} /></div>
                <p className="text-right text-xs text-slate-400">{bucket.eventCount} {" "}{translate("events ·")}{" "}{bucket.uniqueActors} {" "}{translate("actors")}</p>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function AnalyticsExportCard({ csrfToken, eventOptions, scope }: { csrfToken: string; eventOptions: SearchableSelectOption[]; scope: AnalyticsScope }) {
  const [eventName, setEventName] = useState("");
  const [actorId, setActorId] = useState("");
  const [maximumRows, setMaximumRows] = useState(1_000);
  const [busy, setBusy] = useState(false);

  async function exportEvents(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const result = await exportAnalyticsEvents(csrfToken, scope, { actorId, eventName, maximumRows });
      downloadBase64(result.fileName, result.contentType, result.content);
      toast.success(translate(`Exported ${result.exportedRows} analytics events.`));
    } catch (error) {
      toast.error(translate(analyticsErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Controlled export")}</CardTitle>
        <CardDescription>{translate("Download a bounded CSV after applying server-side redaction and spreadsheet neutralization.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-3 sm:grid-cols-3" onSubmit={exportEvents}>
          <SearchableSelect ariaLabel={translate("Export event name")} className={analyticsInputClassName} emptyLabel={translate("All events")} label={translate("Event name")} labelClassName={analyticsLabelClassName} onChange={setEventName} options={eventOptions} value={eventName} />
          <FilterInput label={translate("Actor ID")} name="exportActor" onChange={setActorId} placeholder={translate("Optional")} value={actorId} />
          <label className={analyticsLabelClassName}>{translate("Maximum rows")}<input className={analyticsInputClassName} max={10000} min={1} onChange={(event) => setMaximumRows(event.target.valueAsNumber)} type="number" value={maximumRows} /></label>
          <div className="sm:col-span-3">
            <Button data-ui-action="export-analytics-events" disabled={busy} type="submit" variant="outline">
              {busy ? <LoaderCircle className="size-4 animate-spin" /> : <Download className="size-4" />}
              {translate("Export CSV")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function FilterInput({
  label,
  name,
  onChange,
  placeholder,
  type = "text",
  value,
}: {
  label: string;
  name: string;
  onChange: (value: string) => void;
  placeholder?: string;
  type?: string;
  value: string;
}) {
  return (
    <label className={analyticsLabelClassName}>
      {label}
      <input className={analyticsInputClassName} name={name} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} type={type} value={value} />
    </label>
  );
}

function JsonBlock({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="mb-1.5 text-xs font-medium text-slate-500">{label}</p>
      <pre className="max-h-72 overflow-auto rounded-xl border border-white/8 bg-black/25 p-4 text-xs leading-5 text-slate-300">{formatJson(value)}</pre>
    </div>
  );
}

function formatJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function toOptionalIso(value: string) {
  return value ? new Date(value).toISOString() : undefined;
}

function defaultRange() {
  // datetime-local is rendered at minute precision. Round the upper bound up so
  // events accepted during the current minute are not accidentally excluded.
  const to = new Date(Math.ceil(Date.now() / 60_000) * 60_000);
  const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1_000);
  return { fromAt: toLocalInput(from), toAt: toLocalInput(to) };
}

function toLocalInput(value: Date) {
  const offset = value.getTimezoneOffset() * 60_000;
  return new Date(value.getTime() - offset).toISOString().slice(0, 16);
}

function downloadBase64(fileName: string, contentType: string, content: string) {
  const binary = atob(content);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}
