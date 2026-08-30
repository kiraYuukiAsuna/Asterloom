import { LoaderCircle } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { telemetryErrorMessage } from "@/lib/api/telemetry-management";
import { translate } from "@/lib/i18n/locale";
import { cn } from "@/lib/utils/cn";

export const telemetryInputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-violet-400/45 focus:ring-2 focus:ring-violet-400/15 disabled:opacity-50";
export const telemetryTextAreaClassName = cn(
  telemetryInputClassName,
  "h-28 resize-y py-2.5 font-mono text-xs leading-5",
);
export const telemetryLabelClassName = "grid gap-1.5 text-xs font-medium text-slate-400";

export function TelemetryStatusBadge({ status }: { status: string }) {
  const label = status
    .replace(/^(TELEMETRY_RESOURCE_STATUS_|COLLECTOR_HEALTH_STATUS_)/, "")
    .replaceAll("_", " ")
    .toLowerCase();
  const healthy = status.endsWith("_ACTIVE") || status.endsWith("_HEALTHY");
  return <Badge variant={healthy ? "success" : "planned"}>{translate(label)}</Badge>;
}

export function TelemetryLoading({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-center gap-2 rounded-xl border border-white/8 bg-white/[0.02] p-8 text-sm text-slate-500">
      <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
      {label}
    </div>
  );
}

export function TelemetryEmpty({ message }: { message: string }) {
  return <Card><CardContent className="p-8 text-center text-sm text-slate-500">{message}</CardContent></Card>;
}

export function TelemetryError({ error }: { error: unknown }) {
  return (
    <div className="rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4 text-sm text-rose-200">
      {translate(telemetryErrorMessage(error))}
    </div>
  );
}
