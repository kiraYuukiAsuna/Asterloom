"use client";

import {
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  Download,
  Eye,
  FileClock,
  Filter,
  LoaderCircle,
  RotateCcw,
  Search,
  ShieldAlert,
} from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
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
  auditErrorMessage,
  exportAuditEvents,
  getAuditEvent,
  listAuditEvents,
  type AuditEventRecord,
  type AuditFilters,
  type AuditOutcome,
} from "@/lib/api/audit-management";
import { cn } from "@/lib/utils/cn";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

const inputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const labelClassName =
  "mb-1.5 block text-[11px] font-semibold uppercase tracking-[0.13em] text-slate-500";
const emptyFilters: AuditFilters = {
  actorId: "",
  fromAt: "",
  operation: "",
  outcome: "",
  requestId: "",
  toAt: "",
};

export function AuditWorkspace({ csrfToken }: { csrfToken: string }) {
  const hydrated = useHydrated();
  const [draft, setDraft] = useState<AuditFilters>(emptyFilters);
  const [filters, setFilters] = useState<AuditFilters>(emptyFilters);
  const [pageTokens, setPageTokens] = useState([""]);
  const [selectedEvent, setSelectedEvent] = useState<AuditEventRecord>();
  const [detailPending, setDetailPending] = useState("");
  const [exportPending, setExportPending] = useState(false);
  const pageToken = pageTokens.at(-1) ?? "";

  const events = useSWR(
    [
      "audit-events",
      filters.actorId,
      filters.fromAt,
      filters.operation,
      filters.outcome,
      filters.requestId,
      filters.toAt,
      pageToken,
    ],
    () => listAuditEvents({ ...filters, pageSize: 50, pageToken }),
    { keepPreviousData: true },
  );

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      setFilters({
        ...draft,
        fromAt: toIsoTimestamp(draft.fromAt),
        toAt: toIsoTimestamp(draft.toAt),
      });
      setPageTokens([""]);
      setSelectedEvent(undefined);
    } catch (error) {
      toast.error(translate(auditErrorMessage(error)));
    }
  }

  function clearFilters() {
    setDraft(emptyFilters);
    setFilters(emptyFilters);
    setPageTokens([""]);
    setSelectedEvent(undefined);
  }

  async function showDetail(auditEventId: string) {
    setDetailPending(auditEventId);
    try {
      setSelectedEvent(await getAuditEvent(auditEventId));
    } catch (error) {
      toast.error(translate(auditErrorMessage(error)));
    } finally {
      setDetailPending("");
    }
  }

  async function downloadExport() {
    setExportPending(true);
    try {
      const result = await exportAuditEvents(csrfToken, {
        ...filters,
        maximumRows: 10_000,
      });
      const link = document.createElement("a");
      link.download = result.fileName;
      link.href = `data:${result.contentType};base64,${result.content}`;
      document.body.append(link);
      link.click();
      link.remove();
      toast.success(translate(`Exported ${result.exportedRows} audit events.`));
    } catch (error) {
      toast.error(translate(auditErrorMessage(error)));
    } finally {
      setExportPending(false);
    }
  }

  const page = events.data;

  return (
    <div
      className="space-y-6"
      data-audit-workspace
      data-hydrated={hydrated ? "true" : "false"}
    >
      <section className="grid gap-4 md:grid-cols-3">
        <Metric
          description={translate("Immutable administrative outcomes")}
          icon={FileClock}
          label={translate("Events on this page")}
          value={page?.auditEvents.length.toString() ?? "—"}
        />
        <Metric
          description={translate("Use a request ID to correlate failures")}
          icon={Search}
          label={translate("Request correlation")}
          value={filters.requestId ? "Filtered" : "Ready"}
        />
        <Metric
          description={translate("Values are redacted; only field names remain")}
          icon={ShieldAlert}
          label={translate("Change summaries")}
          value="Redacted"
        />
      </section>

      <Card data-ui-action="list-audit-events">
        <CardHeader className="gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <CardTitle>{translate("Audit trail")}</CardTitle>
            <CardDescription>
              {translate("Search successful, denied, and failed administrative operations.")}</CardDescription>
          </div>
          <Button
            data-ui-action="export-audit-events"
            disabled={!hydrated || exportPending}
            onClick={() => void downloadExport()}
            type="button"
            variant="outline"
          >
            {exportPending ? (
              <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
            ) : (
              <Download aria-hidden="true" className="size-4" />
            )}
            {translate("Export current view")}</Button>
        </CardHeader>
        <CardContent className="space-y-5">
          <form
            className="grid gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 md:grid-cols-2 xl:grid-cols-4"
            onSubmit={applyFilters}
          >
            <FilterField label={translate("Actor")} name="audit-actor">
              <input
                className={inputClassName}
                id="audit-actor"
                name="auditActor"
                onChange={(event) =>
                  setDraft((current) => ({ ...current, actorId: event.target.value }))
                }
                placeholder={translate("Subject or client ID")}
                value={draft.actorId}
              />
            </FilterField>
            <FilterField label={translate("Operation")} name="audit-operation">
              <input
                className={inputClassName}
                id="audit-operation"
                name="auditOperation"
                onChange={(event) =>
                  setDraft((current) => ({ ...current, operation: event.target.value }))
                }
                placeholder={translate("CreateTenant")}
                value={draft.operation}
              />
            </FilterField>
            <FilterField label={translate("Request ID")} name="audit-request-id">
              <input
                className={inputClassName}
                id="audit-request-id"
                name="auditRequestId"
                onChange={(event) =>
                  setDraft((current) => ({ ...current, requestId: event.target.value }))
                }
                placeholder={translate("Trace or request identifier")}
                value={draft.requestId}
              />
            </FilterField>
            <FilterField label={translate("Outcome")} name="audit-outcome">
              <select
                className={inputClassName}
                id="audit-outcome"
                name="auditOutcome"
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    outcome: event.target.value as AuditOutcome | "",
                  }))
                }
                value={draft.outcome}
              >
                <option value="">{translate("All outcomes")}</option>
                <option value="AUDIT_OUTCOME_SUCCEEDED">{translate("Succeeded")}</option>
                <option value="AUDIT_OUTCOME_DENIED">{translate("Denied")}</option>
                <option value="AUDIT_OUTCOME_FAILED">{translate("Failed")}</option>
              </select>
            </FilterField>
            <FilterField label={translate("From")} name="audit-from">
              <input
                className={inputClassName}
                id="audit-from"
                name="auditFrom"
                onChange={(event) =>
                  setDraft((current) => ({ ...current, fromAt: event.target.value }))
                }
                type="datetime-local"
                value={draft.fromAt}
              />
            </FilterField>
            <FilterField label={translate("To")} name="audit-to">
              <input
                className={inputClassName}
                id="audit-to"
                name="auditTo"
                onChange={(event) =>
                  setDraft((current) => ({ ...current, toAt: event.target.value }))
                }
                type="datetime-local"
                value={draft.toAt}
              />
            </FilterField>
            <div className="flex items-end gap-2 md:col-span-2">
              <Button disabled={!hydrated} type="submit">
                <Filter aria-hidden="true" className="size-4" />
                {translate("Apply filters")}</Button>
              <Button
                disabled={!hydrated}
                onClick={clearFilters}
                type="button"
                variant="ghost"
              >
                <RotateCcw aria-hidden="true" className="size-4" />
                {translate("Clear")}</Button>
            </div>
          </form>

          {events.error ? (
            <StateMessage
              description={auditErrorMessage(events.error)}
              icon={CircleAlert}
              title={translate("Audit events could not be loaded")}
            />
          ) : !page && events.isLoading ? (
            <StateMessage
              description={translate("Reading the immutable audit store.")}
              icon={LoaderCircle}
              loading
              title={translate("Loading audit events")}
            />
          ) : page?.auditEvents.length === 0 ? (
            <StateMessage
              description={translate("Adjust the filters or perform an administrative operation.")}
              icon={FileClock}
              title={translate("No matching audit events")}
            />
          ) : (
            <div className="overflow-x-auto rounded-xl border border-white/8">
              <table className="w-full min-w-[62rem] text-left text-sm">
                <thead className="bg-white/[0.035] text-[10px] uppercase tracking-[0.14em] text-slate-500">
                  <tr>
                    <th className="px-4 py-3 font-semibold">{translate("Time")}</th>
                    <th className="px-4 py-3 font-semibold">{translate("Outcome")}</th>
                    <th className="px-4 py-3 font-semibold">{translate("Actor")}</th>
                    <th className="px-4 py-3 font-semibold">{translate("Operation")}</th>
                    <th className="px-4 py-3 font-semibold">{translate("Resource")}</th>
                    <th className="px-4 py-3 font-semibold">{translate("Request")}</th>
                    <th className="px-4 py-3 text-right font-semibold">{translate("Detail")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-white/6">
                  {page?.auditEvents.map((auditEvent) => (
                    <tr
                      className={cn(
                        "transition-colors hover:bg-white/[0.025]",
                        selectedEvent?.id === auditEvent.id && "bg-sky-400/[0.045]",
                      )}
                      data-testid={`audit-event-${auditEvent.id}`}
                      key={auditEvent.id}
                    >
                      <td className="whitespace-nowrap px-4 py-3 text-xs text-slate-400">
                        {formatTimestamp(auditEvent.createdAt)}
                      </td>
                      <td className="px-4 py-3">
                        <OutcomeBadge outcome={auditEvent.outcome} />
                      </td>
                      <td className="max-w-48 truncate px-4 py-3 font-mono text-xs text-slate-300">
                        {auditEvent.actorId}
                      </td>
                      <td className="px-4 py-3 text-slate-200">
                        {operationName(auditEvent.operation)}
                      </td>
                      <td className="px-4 py-3">
                        <p className="text-slate-300">{auditEvent.resourceType}</p>
                        <p className="max-w-44 truncate font-mono text-[11px] text-slate-600">
                          {auditEvent.resourceId || "—"}
                        </p>
                      </td>
                      <td className="max-w-48 truncate px-4 py-3 font-mono text-[11px] text-slate-500">
                        {auditEvent.requestId}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <Button
                          aria-label={translate(`View audit event ${auditEvent.id}`)}
                          data-ui-action="get-audit-event"
                          disabled={detailPending !== ""}
                          onClick={() => void showDetail(auditEvent.id)}
                          size="sm"
                          type="button"
                          variant="ghost"
                        >
                          {detailPending === auditEvent.id ? (
                            <LoaderCircle className="size-3.5 animate-spin" />
                          ) : (
                            <Eye className="size-3.5" />
                          )}
                          {translate("View")}</Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="flex items-center justify-between">
            <p className="text-xs text-slate-600">{translate("Page")}{" "}{pageTokens.length}</p>
            <div className="flex gap-2">
              <Button
                disabled={pageTokens.length === 1 || events.isLoading}
                onClick={() =>
                  setPageTokens((current) => current.slice(0, Math.max(1, current.length - 1)))
                }
                size="sm"
                type="button"
                variant="outline"
              >
                <ChevronLeft className="size-3.5" />
                {translate("Previous")}</Button>
              <Button
                disabled={!page?.nextPageToken || events.isLoading}
                onClick={() =>
                  page?.nextPageToken &&
                  setPageTokens((current) => [...current, page.nextPageToken!])
                }
                size="sm"
                type="button"
                variant="outline"
              >
                {translate("Next")}<ChevronRight className="size-3.5" />
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <AuditDetail auditEvent={selectedEvent} onCorrelate={(requestId) => {
        setDraft({ ...emptyFilters, requestId });
        setFilters({ ...emptyFilters, requestId });
        setPageTokens([""]);
      }} />
    </div>
  );
}

function AuditDetail({
  auditEvent,
  onCorrelate,
}: {
  auditEvent?: AuditEventRecord;
  onCorrelate: (requestId: string) => void;
}) {
  return (
    <Card data-testid="audit-event-detail">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>{translate("Event detail")}</CardTitle>
          <CardDescription>
            {translate("Full scope, correlation, result, and redacted change metadata.")}</CardDescription>
        </div>
        {auditEvent && <OutcomeBadge outcome={auditEvent.outcome} />}
      </CardHeader>
      <CardContent>
        {!auditEvent ? (
          <StateMessage
            description={translate("Choose View on an event to load it through the detail API.")}
            icon={Eye}
            title={translate("No event selected")}
          />
        ) : (
          <div className="grid gap-x-8 gap-y-5 md:grid-cols-2 xl:grid-cols-3">
            <Detail label={translate("Event ID")} value={auditEvent.id} mono />
            <Detail label={translate("Occurred at")} value={formatTimestamp(auditEvent.createdAt)} />
            <Detail label={translate("Actor")} value={auditEvent.actorId} mono />
            <Detail label={translate("Operation")} value={auditEvent.operation} mono />
            <Detail
              label={translate("Resource")}
              value={`${auditEvent.resourceType}${auditEvent.resourceId ? ` · ${auditEvent.resourceId}` : ""}`}
              mono
            />
            <Detail
              label={translate("Error code")}
              value={auditEvent.errorCode || "No error"}
              mono
            />
            <Detail label={translate("Tenant scope")} value={auditEvent.tenantId ?? "Global"} mono />
            <Detail
              label={translate("Application scope")}
              value={auditEvent.applicationId ?? "All applications"}
              mono
            />
            <Detail
              label={translate("Environment scope")}
              value={auditEvent.environmentId ?? "All environments"}
              mono
            />
            <div className="md:col-span-2 xl:col-span-3">
              <p className={labelClassName}>{translate("Request correlation")}</p>
              <div className="flex flex-wrap items-center gap-2">
                <code className="rounded-lg border border-white/8 bg-black/20 px-3 py-2 text-xs text-slate-300">
                  {auditEvent.requestId}
                </code>
                <Button
                  onClick={() => onCorrelate(auditEvent.requestId)}
                  size="sm"
                  type="button"
                  variant="outline"
                >
                  <Search className="size-3.5" />
                  {translate("Show correlated events")}</Button>
              </div>
            </div>
            <div className="md:col-span-2 xl:col-span-3">
              <p className={labelClassName}>{translate("Redacted change summary")}</p>
              <code className="block overflow-x-auto rounded-xl border border-white/8 bg-black/25 p-4 text-xs leading-6 text-slate-300">
                {auditEvent.changeSummary}
              </code>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function Metric({
  description,
  icon: Icon,
  label,
  value,
}: {
  description: string;
  icon: typeof FileClock;
  label: string;
  value: string;
}) {
  return (
    <Card className="p-5">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-[0.15em] text-slate-500">
            {label}
          </p>
          <p className="mt-2 text-2xl font-semibold tracking-tight text-white">{value}</p>
          <p className="mt-1 text-xs leading-5 text-slate-500">{description}</p>
        </div>
        <div className="grid size-9 shrink-0 place-items-center rounded-xl border border-sky-300/15 bg-sky-400/8 text-sky-300">
          <Icon className="size-4" />
        </div>
      </div>
    </Card>
  );
}

function FilterField({
  children,
  label,
  name,
}: {
  children: React.ReactNode;
  label: string;
  name: string;
}) {
  return (
    <div>
      <label className={labelClassName} htmlFor={name}>
        {label}
      </label>
      {children}
    </div>
  );
}

function Detail({ label, mono = false, value }: { label: string; mono?: boolean; value: string }) {
  return (
    <div className="min-w-0">
      <p className={labelClassName}>{label}</p>
      <p className={cn("break-words text-sm text-slate-200", mono && "font-mono text-xs")}>
        {value}
      </p>
    </div>
  );
}

function OutcomeBadge({ outcome }: { outcome: AuditOutcome }) {
  const succeeded = outcome === "AUDIT_OUTCOME_SUCCEEDED";
  const denied = outcome === "AUDIT_OUTCOME_DENIED";
  return (
    <Badge
      className={cn(
        denied && "border-amber-400/20 bg-amber-400/10 text-amber-300",
        !succeeded && !denied && "border-rose-400/20 bg-rose-400/10 text-rose-300",
      )}
      variant={succeeded ? "success" : "planned"}
    >
      {translate(succeeded ? "Succeeded" : denied ? "Denied" : "Failed")}
    </Badge>
  );
}

function StateMessage({
  description,
  icon: Icon,
  loading = false,
  title,
}: {
  description: string;
  icon: typeof FileClock;
  loading?: boolean;
  title: string;
}) {
  return (
    <div className="grid min-h-40 place-items-center rounded-xl border border-dashed border-white/10 bg-white/[0.015] p-6 text-center">
      <div>
        <Icon
          className={cn("mx-auto size-5 text-slate-500", loading && "animate-spin")}
        />
        <p className="mt-3 text-sm font-medium text-slate-300">{translate(title)}</p>
        <p className="mt-1 text-xs text-slate-500">{translate(description)}</p>
      </div>
    </div>
  );
}

function operationName(operation: string) {
  return operation.slice(operation.lastIndexOf("/") + 1);
}

function formatTimestamp(value: string) {
  return formatDateTime(value, {
    dateStyle: "medium",
    timeStyle: "medium",
  });
}

function toIsoTimestamp(value: string) {
  if (!value) return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    throw new Error("The date and time filter is invalid.");
  }
  return parsed.toISOString();
}
