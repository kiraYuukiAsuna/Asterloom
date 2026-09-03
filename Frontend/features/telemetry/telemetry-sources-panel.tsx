"use client";

import { Archive, Eye, LoaderCircle, Plus, RotateCcw, Save, Search, Settings2 } from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TelemetryResourceStatusObject } from "@/lib/api/generated/models";
import {
  archiveTelemetrySource,
  createTelemetrySource,
  getTelemetrySettings,
  getTelemetrySource,
  listTelemetrySources,
  restoreTelemetrySource,
  telemetryErrorMessage,
  type TelemetryScope,
  type TelemetrySettingsRecord,
  type TelemetrySourceRecord,
  updateTelemetrySettings,
  updateTelemetrySource,
} from "@/lib/api/telemetry-management";
import { cn } from "@/lib/utils/cn";

import {
  TelemetryEmpty,
  TelemetryError,
  TelemetryLoading,
  TelemetryStatusBadge,
  telemetryInputClassName,
  telemetryLabelClassName,
  telemetryTextAreaClassName,
} from "./telemetry-ui";
import { translate } from "@/lib/i18n/locale";

const defaultAttributes = JSON.stringify({ "team.name": "platform" }, null, 2);

export function TelemetrySourcesPanel({ csrfToken, scope }: { csrfToken: string; scope: TelemetryScope }) {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [queryDraft, setQueryDraft] = useState("");
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<TelemetrySourceRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const sources = useSWR(
    ["telemetry-sources", scope.tenantId, scope.applicationId, scope.environmentId, includeArchived, query],
    () => listTelemetrySources(scope, { includeArchived, pageSize: 100, query }),
  );
  const settings = useSWR(
    ["telemetry-settings", scope.tenantId, scope.applicationId, scope.environmentId],
    () => getTelemetrySettings(scope),
  );

  async function inspect(source: TelemetrySourceRecord) {
    setGettingId(source.id);
    try {
      setSelected(await getTelemetrySource(scope, source.id));
      toast.success(translate("Telemetry source refreshed."));
    } catch (error) {
      toast.error(translate(telemetryErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  async function sourceChanged(source: TelemetrySourceRecord) {
    setSelected(source);
    await sources.mutate();
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.2fr)_minmax(22rem,0.8fr)]">
        <Card data-ui-action="list-telemetry-sources">
          <CardHeader>
            <CardTitle>{translate("Registered sources")}</CardTitle>
            <CardDescription>{translate("Stable service identities and bounded resource attributes for this environment.")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <form className="flex flex-col gap-3 sm:flex-row" onSubmit={(event) => { event.preventDefault(); setQuery(queryDraft); }}>
              <input aria-label={translate("Search telemetry sources")} className={telemetryInputClassName} onChange={(event) => setQueryDraft(event.target.value)} placeholder={translate("Search key, name, or service")} value={queryDraft} />
              <Button type="submit" variant="outline"><Search className="size-4" /> {" "}{translate("Search")}</Button>
              <label className="flex shrink-0 items-center gap-2 text-xs text-slate-400">
                <input checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} type="checkbox" /> {translate("Include archived")}</label>
            </form>
            {sources.isLoading ? <TelemetryLoading label={translate("Loading telemetry sources")} />
              : sources.error ? <TelemetryError error={sources.error} />
              : (sources.data?.sources.length ?? 0) === 0 ? <TelemetryEmpty message={translate("No telemetry sources match this environment.")} />
              : <div className="space-y-2">{sources.data?.sources.map((source) => (
                  <article className={cn("rounded-xl border p-4", selected?.id === source.id ? "border-violet-400/30 bg-violet-400/[0.06]" : "border-white/8 bg-white/[0.02]")} data-testid={`telemetry-source-${source.key}`} key={source.id}>
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <button className="min-w-0 text-left" onClick={() => setSelected(source)} type="button">
                        <div className="flex items-center gap-2"><p className="font-medium text-white">{source.displayName}</p><TelemetryStatusBadge status={source.status} /></div>
                        <p className="mt-1 font-mono text-xs text-violet-200">{source.serviceName}</p>
                        <p className="mt-1 text-xs text-slate-600">{source.key} {" "}{translate("· version")}{" "}{source.version}</p>
                      </button>
                      <Button data-ui-action="get-telemetry-source" disabled={gettingId === source.id} onClick={() => void inspect(source)} size="sm" type="button" variant="outline">
                        {gettingId === source.id ? <LoaderCircle className="size-3.5 animate-spin" /> : <Eye className="size-3.5" />} {translate("Inspect")}</Button>
                    </div>
                  </article>
                ))}</div>}
          </CardContent>
        </Card>
        {selected ? <SourceInspector csrfToken={csrfToken} key={`${selected.id}:${selected.version}`} onChanged={sourceChanged} source={selected} />
          : <CreateSourceCard csrfToken={csrfToken} onCreated={sourceChanged} scope={scope} />}
      </div>

      {selected && <CreateSourceCard csrfToken={csrfToken} onCreated={sourceChanged} scope={scope} />}

      {settings.isLoading ? <TelemetryLoading label={translate("Loading telemetry settings")} />
        : settings.error ? <TelemetryError error={settings.error} />
        : settings.data ? <TelemetrySettingsCard csrfToken={csrfToken} key={settings.data.version} onChanged={async (value) => { await settings.mutate(value, { revalidate: false }); }} settings={settings.data} />
        : null}
    </div>
  );
}

function CreateSourceCard({ csrfToken, onCreated, scope }: { csrfToken: string; onCreated: (source: TelemetrySourceRecord) => Promise<void>; scope: TelemetryScope }) {
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [serviceName, setServiceName] = useState("");
  const [attributes, setAttributes] = useState(defaultAttributes);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true);
    try {
      const source = await createTelemetrySource(csrfToken, scope, { key, displayName, description, serviceName, resourceAttributesJson: attributes });
      setKey(""); setDisplayName(""); setDescription(""); setServiceName(""); setAttributes(defaultAttributes);
      await onCreated(source); toast.success(translate("Telemetry source created."));
    } catch (error) { toast.error(translate(telemetryErrorMessage(error))); } finally { setBusy(false); }
  }
  return (
    <Card className="h-fit">
      <CardHeader><CardTitle>{translate("Register source")}</CardTitle><CardDescription>{translate("Define the OTel service identity before deploying its SDK configuration.")}</CardDescription></CardHeader>
      <CardContent>
        <form className="grid gap-4 md:grid-cols-2" onSubmit={submit}>
          <label className={telemetryLabelClassName}>{translate("Key")}<input className={telemetryInputClassName} name="telemetrySourceKey" onChange={(event) => setKey(event.target.value)} placeholder={translate("checkout-api")} value={key} /></label>
          <label className={telemetryLabelClassName}>{translate("Display name")}<input className={telemetryInputClassName} name="telemetrySourceDisplayName" onChange={(event) => setDisplayName(event.target.value)} placeholder={translate("Checkout API")} value={displayName} /></label>
          <label className={telemetryLabelClassName}>{translate("Service name")}<input className={telemetryInputClassName} name="telemetrySourceServiceName" onChange={(event) => setServiceName(event.target.value)} placeholder={translate("asterloom.checkout-api")} value={serviceName} /></label>
          <label className={telemetryLabelClassName}>{translate("Description")}<input className={telemetryInputClassName} name="telemetrySourceDescription" onChange={(event) => setDescription(event.target.value)} value={description} /></label>
          <label className={cn(telemetryLabelClassName, "md:col-span-2")}>{translate("Resource attributes")}<textarea className={telemetryTextAreaClassName} name="telemetrySourceAttributes" onChange={(event) => setAttributes(event.target.value)} value={attributes} /></label>
          <div className="md:col-span-2"><Button data-ui-action="create-telemetry-source" disabled={busy} type="submit">{busy ? <LoaderCircle className="size-4 animate-spin" /> : <Plus className="size-4" />} {" "}{translate("Register source")}</Button></div>
        </form>
      </CardContent>
    </Card>
  );
}

function SourceInspector({ csrfToken, onChanged, source }: { csrfToken: string; onChanged: (source: TelemetrySourceRecord) => Promise<void>; source: TelemetrySourceRecord }) {
  const [busy, setBusy] = useState("");
  const [displayName, setDisplayName] = useState(source.displayName);
  const [description, setDescription] = useState(source.description);
  const [serviceName, setServiceName] = useState(source.serviceName);
  const [attributes, setAttributes] = useState(() => formatJson(source.resourceAttributesJson));
  const active = source.status === TelemetryResourceStatusObject.TELEMETRY_RESOURCE_STATUS_ACTIVE;
  async function perform(name: string, action: () => Promise<TelemetrySourceRecord>, message: string) {
    setBusy(name); try { await onChanged(await action()); toast.success(translate(message)); } catch (error) { toast.error(translate(telemetryErrorMessage(error))); } finally { setBusy(""); }
  }
  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader><div className="flex items-center justify-between gap-3"><CardTitle>{source.displayName}</CardTitle><TelemetryStatusBadge status={source.status} /></div><CardDescription><span className="block font-mono">{source.key}</span><span className="mt-1 block break-all font-mono text-[10px] text-slate-600">{source.id}</span></CardDescription></CardHeader>
      <CardContent className="space-y-4">
        <label className={telemetryLabelClassName}>{translate("Display name")}<input className={telemetryInputClassName} disabled={!active} name="editTelemetrySourceDisplayName" onChange={(event) => setDisplayName(event.target.value)} value={displayName} /></label>
        <label className={telemetryLabelClassName}>{translate("Service name")}<input className={telemetryInputClassName} disabled={!active} name="editTelemetrySourceServiceName" onChange={(event) => setServiceName(event.target.value)} value={serviceName} /></label>
        <label className={telemetryLabelClassName}>{translate("Description")}<textarea className={cn(telemetryTextAreaClassName, "h-20 font-sans")} disabled={!active} name="editTelemetrySourceDescription" onChange={(event) => setDescription(event.target.value)} value={description} /></label>
        <label className={telemetryLabelClassName}>{translate("Resource attributes")}<textarea className={telemetryTextAreaClassName} disabled={!active} name="editTelemetrySourceAttributes" onChange={(event) => setAttributes(event.target.value)} value={attributes} /></label>
        {active ? <div className="flex flex-wrap gap-2">
          <Button data-ui-action="update-telemetry-source" disabled={Boolean(busy)} onClick={() => void perform("save", () => updateTelemetrySource(csrfToken, source, { displayName, description, serviceName, resourceAttributesJson: attributes }), "Telemetry source updated.")} type="button"><Save className="size-4" /> {" "}{translate("Save")}</Button>
          <Button data-ui-action="archive-telemetry-source" disabled={Boolean(busy)} onClick={() => void perform("archive", () => archiveTelemetrySource(csrfToken, source), "Telemetry source archived.")} type="button" variant="outline"><Archive className="size-4" /> {" "}{translate("Archive")}</Button>
        </div> : <Button data-ui-action="restore-telemetry-source" disabled={Boolean(busy)} onClick={() => void perform("restore", () => restoreTelemetrySource(csrfToken, source), "Telemetry source restored.")} type="button"><RotateCcw className="size-4" /> {" "}{translate("Restore source")}</Button>}
      </CardContent>
    </Card>
  );
}

function TelemetrySettingsCard({ csrfToken, onChanged, settings }: { csrfToken: string; onChanged: (settings: TelemetrySettingsRecord) => Promise<void>; settings: TelemetrySettingsRecord }) {
  const [busy, setBusy] = useState(false);
  const [samplingRatio, setSamplingRatio] = useState(settings.samplingRatio);
  const [tracesEnabled, setTracesEnabled] = useState(settings.tracesEnabled);
  const [metricsEnabled, setMetricsEnabled] = useState(settings.metricsEnabled);
  const [logsEnabled, setLogsEnabled] = useState(settings.logsEnabled);
  const [diagnosticsBaseUrl, setDiagnosticsBaseUrl] = useState(settings.diagnosticsBaseUrl);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true);
    try {
      await onChanged(await updateTelemetrySettings(csrfToken, settings, { samplingRatio, tracesEnabled, metricsEnabled, logsEnabled, exporterEndpoint: settings.exporterEndpoint, exporterProtocol: settings.exporterProtocol, diagnosticsBaseUrl }));
      toast.success(translate("Telemetry sampling and storage settings updated."));
    } catch (error) { toast.error(translate(telemetryErrorMessage(error))); } finally { setBusy(false); }
  }
  return (
    <Card data-ui-action="get-telemetry-settings">
      <CardHeader><div className="flex items-center gap-2"><Settings2 className="size-4 text-violet-300" /><CardTitle>{translate("Sampling and database storage")}</CardTitle></div><CardDescription>{translate("Controls which signals are retained in PostgreSQL for this environment.")}</CardDescription></CardHeader>
      <CardContent><form className="grid gap-4 md:grid-cols-2 xl:grid-cols-4" onSubmit={submit}>
        <label className={telemetryLabelClassName}>{translate("Sampling ratio")}<input className={telemetryInputClassName} max={1} min={0} name="telemetrySamplingRatio" onChange={(event) => setSamplingRatio(event.target.valueAsNumber)} step={0.01} type="number" value={samplingRatio} /></label>
        <label className={cn(telemetryLabelClassName, "md:col-span-1 xl:col-span-3")}>{translate("Diagnostics base URL")}<input className={telemetryInputClassName} name="telemetryDiagnosticsBaseUrl" onChange={(event) => setDiagnosticsBaseUrl(event.target.value)} placeholder={translate("https://observability.example/traces")} value={diagnosticsBaseUrl} /></label>
        <div className="flex flex-wrap gap-5 md:col-span-2 xl:col-span-4">
          <SignalToggle checked={tracesEnabled} label={translate("Traces")} onChange={setTracesEnabled} />
          <SignalToggle checked={metricsEnabled} label={translate("Metrics")} onChange={setMetricsEnabled} />
          <SignalToggle checked={logsEnabled} label={translate("Logs")} onChange={setLogsEnabled} />
        </div>
        <div className="md:col-span-2 xl:col-span-4"><Button data-ui-action="update-telemetry-settings" disabled={busy} type="submit">{busy ? <LoaderCircle className="size-4 animate-spin" /> : <Save className="size-4" />} {" "}{translate("Save telemetry policy")}</Button></div>
      </form></CardContent>
    </Card>
  );
}

function SignalToggle({ checked, label, onChange }: { checked: boolean; label: string; onChange: (value: boolean) => void }) {
  return <label className="flex items-center gap-2 text-sm text-slate-300"><input checked={checked} onChange={(event) => onChange(event.target.checked)} type="checkbox" /> {label}</label>;
}

function formatJson(value: string) {
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}
