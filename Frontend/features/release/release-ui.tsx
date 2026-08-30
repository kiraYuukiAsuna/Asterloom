import { LoaderCircle } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { releaseErrorMessage } from "@/lib/api/release-management";
import { translate } from "@/lib/i18n/locale";
import { cn } from "@/lib/utils/cn";

export const releaseInputClassName =
  "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-violet-400/45 focus:ring-2 focus:ring-violet-400/15 disabled:opacity-50";
export const releaseTextAreaClassName = cn(
  releaseInputClassName,
  "h-24 resize-y py-2.5",
);
export const releaseLabelClassName = "grid gap-1.5 text-xs font-medium text-slate-400";

export function ReleaseStatusBadge({ status }: { status: string }) {
  const label = status
    .replace(/^(RELEASE_(SIGNING_KEY|CHANNEL|ARTIFACT)_STATUS_|DESKTOP_RELEASE_STATUS_)/, "")
    .replaceAll("_", " ")
    .toLowerCase();
  const variant = status.endsWith("_ACTIVE") || status.endsWith("_VERIFIED") || status.endsWith("_PUBLISHED")
    ? "success"
    : status.endsWith("_DRAFT") || status.endsWith("_UPLOADING")
      ? "info"
      : "planned";
  return (
    <Badge
      className={status.endsWith("_REJECTED") ? "border-rose-400/20 bg-rose-400/10 text-rose-300" : undefined}
      variant={variant}
    >
      {translate(label)}
    </Badge>
  );
}

export function ReleaseLoadingState({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-center gap-2 rounded-xl border border-white/8 bg-white/[0.02] p-8 text-sm text-slate-500">
      <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
      {label}
    </div>
  );
}

export function ReleaseEmptyState({ message }: { message: string }) {
  return (
    <Card>
      <CardContent className="p-8 text-center text-sm text-slate-500">
        {message}
      </CardContent>
    </Card>
  );
}

export function ReleaseErrorState({ error }: { error: unknown }) {
  return (
    <div className="rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4 text-sm text-rose-200">
      {translate(releaseErrorMessage(error))}
    </div>
  );
}
