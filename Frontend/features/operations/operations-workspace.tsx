"use client";

import { Activity, Braces, Download, FileJson, RefreshCw, Search } from "lucide-react";
import Link from "next/link";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  getOperationsHealth,
  getOperationsOpenApiDocument,
  listOperationsApis,
  operationsErrorMessage,
} from "@/lib/api/operations-management";
import { cn } from "@/lib/utils/cn";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

const inputClassName = "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15";

export function OperationsWorkspace({ view }: { view: "apis" | "health" }) {
  return (
    <div className="space-y-6">
      <section className="theme-hero-sky flex flex-col gap-5 rounded-2xl border border-sky-400/15 bg-gradient-to-br from-sky-400/[0.09] via-slate-950/60 to-emerald-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div><Badge variant="info">{translate("Runtime operations")}</Badge><h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">{translate("API and health operations")}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">{translate("Inspect the live Protobuf/HTTP contract, retrieve the canonical OpenAPI document, and verify registered dependency health.")}</p></div>
        <nav aria-label={translate("Operations views")} className="flex rounded-xl border border-white/10 p-1">
          <OperationsTab active={view === "apis"} href="/operations/apis" icon={Braces} label={translate("API catalog")} />
          <OperationsTab active={view === "health"} href="/operations/health" icon={Activity} label={translate("Health")} />
        </nav>
      </section>
      {view === "apis" ? <ApiCatalogPanel /> : <OperationsHealthPanel />}
    </div>
  );
}

function ApiCatalogPanel() {
  const [queryDraft, setQueryDraft] = useState("");
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<"" | "admin" | "runtime">("");
  const [downloading, setDownloading] = useState(false);
  const [documentHash, setDocumentHash] = useState("");
  const apis = useSWR(["operations-apis", query, category], () => listOperationsApis({ query, category }));

  async function downloadOpenApi() {
    setDownloading(true);
    try {
      const document = await getOperationsOpenApiDocument();
      const url = URL.createObjectURL(new Blob([document.content], { type: document.contentType }));
      const anchor = window.document.createElement("a");
      anchor.href = url; anchor.download = "asterloom-v1.openapi.json"; anchor.click();
      URL.revokeObjectURL(url); setDocumentHash(document.sha256);
      toast.success(translate("Canonical OpenAPI document downloaded."));
    } catch (error) { toast.error(translate(operationsErrorMessage(error))); } finally { setDownloading(false); }
  }

  return (
    <Card data-ui-action="list-operation-apis">
      <CardHeader className="sm:flex-row sm:items-end sm:justify-between"><div><CardTitle>{translate("Live API catalog")}</CardTitle><CardDescription>{translate("Every custom RPC listed here has a JSON Transcoding route and appears in generated OpenAPI.")}</CardDescription></div><Button data-ui-action="get-operations-openapi" disabled={downloading} onClick={() => void downloadOpenApi()} type="button" variant="outline">{downloading ? <RefreshCw className="size-4 animate-spin" /> : <Download className="size-4" />} {" "}{translate("Download OpenAPI")}</Button></CardHeader>
      <CardContent className="space-y-4">
        <form className="grid gap-3 sm:grid-cols-[1fr_10rem_auto]" onSubmit={(event: FormEvent) => { event.preventDefault(); setQuery(queryDraft); }}>
          <input aria-label={translate("Search API catalog")} className={inputClassName} onChange={(event) => setQueryDraft(event.target.value)} placeholder={translate("Service, RPC, or HTTP path")} value={queryDraft} />
          <select aria-label={translate("API category")} className={inputClassName} onChange={(event) => setCategory(event.target.value as typeof category)} value={category}><option value="">{translate("All APIs")}</option><option value="admin">{translate("Admin")}</option><option value="runtime">{translate("Runtime")}</option></select>
          <Button type="submit" variant="outline"><Search className="size-4" /> {" "}{translate("Search")}</Button>
        </form>
        {documentHash && <div className="rounded-lg border border-emerald-400/15 bg-emerald-400/[0.05] px-3 py-2 font-mono text-[11px] text-emerald-200" data-testid="operations-openapi-hash">{translate("SHA-256")}{" "}{documentHash}</div>}
        {apis.isLoading ? <Loading label={translate("Loading API catalog")} /> : apis.error ? <ErrorBlock error={apis.error} /> : (apis.data?.apis.length ?? 0) === 0 ? <Empty message={translate("No APIs match this filter.")} /> : <div className="overflow-x-auto rounded-xl border border-white/8"><table className="w-full min-w-[760px] text-left text-xs"><thead className="bg-white/[0.03] text-slate-500"><tr><th className="px-4 py-3">{translate("HTTP")}</th><th className="px-4 py-3">{translate("Service / RPC")}</th><th className="px-4 py-3">{translate("Category")}</th><th className="px-4 py-3">{translate("Types")}</th></tr></thead><tbody>{apis.data?.apis.map((api) => <tr className="border-t border-white/6" data-testid={`operations-api-${api.service}-${api.rpc}`} key={`${api.service}/${api.rpc}`}><td className="px-4 py-3"><span className="font-mono font-medium text-sky-300">{api.httpMethod}</span><p className="mt-1 max-w-sm break-all font-mono text-[11px] text-slate-500">{api.httpPath}</p></td><td className="px-4 py-3"><p className="text-slate-300">{api.service}</p><p className="mt-1 font-mono text-slate-500">{api.rpc}</p></td><td className="px-4 py-3"><Badge variant={api.category === "admin" ? "info" : "success"}>{translate(api.category)}</Badge></td><td className="px-4 py-3 font-mono text-[10px] text-slate-600"><p>{api.requestType}</p><p className="mt-1">→ {api.responseType}</p></td></tr>)}</tbody></table></div>}
      </CardContent>
    </Card>
  );
}

function OperationsHealthPanel() {
  const health = useSWR("operations-health", getOperationsHealth, { dedupingInterval: 0 });
  return (
    <Card data-ui-action="get-operations-health">
      <CardHeader className="flex-row items-start justify-between gap-4"><div><CardTitle>{translate("Registered dependency health")}</CardTitle><CardDescription>{translate("This is the detailed view behind readiness and startup probes; observability backends remain non-critical.")}</CardDescription></div><Button aria-label={translate("Refresh operations health")} onClick={() => void health.mutate()} size="sm" type="button" variant="outline"><RefreshCw className="size-3.5" /></Button></CardHeader>
      <CardContent>{health.isLoading ? <Loading label={translate("Loading dependency health")} /> : health.error ? <ErrorBlock error={health.error} /> : health.data ? <div className="space-y-4" data-testid="operations-health"><div className="flex items-center justify-between rounded-xl border border-white/8 bg-white/[0.02] p-4"><div><p className="text-sm font-medium text-white">{translate("Asterloom.Server")}</p><p className="mt-1 text-xs text-slate-500">{translate("Checked")}{" "}{formatDateTime(health.data.checkedAt)} · {health.data.durationMilliseconds} {" "}{translate("ms")}</p></div><HealthBadge status={health.data.status} /></div><div className="grid gap-3 md:grid-cols-2">{health.data.dependencies.map((dependency) => <article className="rounded-xl border border-white/8 bg-white/[0.02] p-4" data-testid={`operations-dependency-${dependency.name}`} key={dependency.name}><div className="flex items-center justify-between gap-3"><p className="font-medium text-slate-200">{dependency.name}</p><HealthBadge status={dependency.status} /></div><p className="mt-2 text-xs text-slate-500">{translate(dependency.description || "No additional detail.")}</p><p className="mt-3 font-mono text-[11px] text-slate-600">{dependency.durationMilliseconds} {" "}{translate("ms")}{" "}{translate(dependency.tags.length > 0 ? `· ${dependency.tags.join(", ")}` : "")}</p></article>)}</div></div> : null}</CardContent>
    </Card>
  );
}

function OperationsTab({ active, href, icon: Icon, label }: { active: boolean; href: string; icon: typeof Activity; label: string }) { return <Link className={cn("flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-medium transition", active ? "bg-sky-400/15 text-sky-100" : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200")} href={href}><Icon className="size-3.5" />{label}</Link>; }
function HealthBadge({ status }: { status: string }) { const label = status.replace("DEPENDENCY_HEALTH_STATUS_", "").toLowerCase(); return <Badge variant={status.endsWith("_HEALTHY") ? "success" : "planned"}>{translate(label)}</Badge>; }
function Loading({ label }: { label: string }) { return <div className="flex items-center justify-center gap-2 rounded-xl border border-white/8 p-8 text-sm text-slate-500"><RefreshCw className="size-4 animate-spin" />{label}</div>; }
function Empty({ message }: { message: string }) { return <div className="rounded-xl border border-white/8 p-8 text-center text-sm text-slate-500"><FileJson className="mx-auto mb-3 size-5" />{message}</div>; }
function ErrorBlock({ error }: { error: unknown }) { return <div className="rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4 text-sm text-rose-200">{translate(operationsErrorMessage(error))}</div>; }
