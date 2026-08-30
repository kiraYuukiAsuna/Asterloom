"use client";

import {
  Activity,
  Check,
  Clock3,
  RefreshCw,
  Server,
  Sparkles,
} from "lucide-react";
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
import { getPlatformInfo } from "@/lib/api/platform";
import { translate } from "@/lib/i18n/locale";
import { formatDateTime } from "@/lib/i18n/format";

export type CapabilityView = {
  key: string;
  displayName: string;
  lifecycle: string;
};

export function CapabilityGrid({
  capabilities,
}: {
  capabilities: CapabilityView[];
}) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      {capabilities.map((capability) => {
        const available =
          capability.lifecycle === "CAPABILITY_LIFECYCLE_AVAILABLE";

        return (
          <div
            className="group flex min-w-0 items-center gap-3 overflow-hidden rounded-xl border border-white/8 bg-white/[0.025] p-4 transition-colors hover:border-white/14 hover:bg-white/[0.04]"
            data-testid={"capability-" + capability.key}
            key={capability.key}
          >
            <div
              className={
                available
                  ? "grid size-9 place-items-center rounded-lg bg-emerald-400/10 text-emerald-300"
                  : "grid size-9 place-items-center rounded-lg bg-slate-400/5 text-slate-600"
              }
            >
              {available ? (
                <Check aria-hidden="true" className="size-4" />
              ) : (
                <Clock3 aria-hidden="true" className="size-4" />
              )}
            </div>
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-slate-200">
                {capability.displayName}
              </p>
              <p className="mt-0.5 font-mono text-[10px] text-slate-600">
                {capability.key}
              </p>
            </div>
            <Badge
              className="ml-auto shrink-0"
              variant={available ? "success" : "planned"}
            >
              {translate(available ? "Ready" : "Planned")}
            </Badge>
          </div>
        );
      })}
    </div>
  );
}

export function PlatformOverview() {
  const { data, error, isLoading, mutate } = useSWR(
    "platform-info",
    getPlatformInfo,
    {
      refreshInterval: 30_000,
      revalidateOnFocus: true,
    },
  );

  if (isLoading) {
    return <PlatformOverviewSkeleton />;
  }

  if (error || !data) {
    return (
      <Card className="border-rose-400/15">
        <CardHeader>
          <CardTitle>{translate("Server connection unavailable")}</CardTitle>
          <CardDescription>
            {translate("The Web Console could not reach Asterloom.Server through the BFF.")}</CardDescription>
        </CardHeader>
        <CardContent>
          <Button
            onClick={() => {
              toast.promise(mutate(), {
                loading: translate("Retrying connection…"),
                success: translate("Asterloom.Server is reachable."),
                error: translate("The server is still unavailable."),
              });
            }}
            type="button"
          >
            <RefreshCw aria-hidden="true" className="size-4" />
            {translate("Retry")}</Button>
        </CardContent>
      </Card>
    );
  }

  const capabilities: CapabilityView[] = (data.capabilities ?? []).map(
    (capability) => ({
      key: capability.key ?? "unknown",
      displayName: capability.displayName ?? "Unnamed capability",
      lifecycle:
        capability.lifecycle ?? "CAPABILITY_LIFECYCLE_UNSPECIFIED",
    }),
  );
  const availableCount = capabilities.filter(
    (capability) =>
      capability.lifecycle === "CAPABILITY_LIFECYCLE_AVAILABLE",
  ).length;
  const serverTime = data.serverTime
    ? formatDateTime(data.serverTime, {
        dateStyle: "medium",
        timeStyle: "medium",
      })
    : translate("Unavailable");

  return (
    <div className="space-y-6" data-ui-action="view-platform-info">
      <section className="theme-hero-sky relative overflow-hidden rounded-3xl border border-sky-300/10 bg-[linear-gradient(135deg,rgba(14,165,233,0.13),rgba(15,23,42,0.78)_45%,rgba(2,6,23,0.95))] p-6 shadow-[0_30px_100px_-55px_rgba(56,189,248,0.8)] sm:p-9">
        <div className="absolute -right-24 -top-32 size-72 rounded-full bg-sky-400/10 blur-3xl" />
        <div className="relative max-w-3xl">
          <div className="flex items-center gap-2">
            <Badge variant="success">
              <span className="size-1.5 rounded-full bg-emerald-300 shadow-[0_0_12px_rgba(110,231,183,0.9)]" />
              {translate("Operational")}</Badge>
            <span className="font-mono text-xs text-slate-500">
              {translate("v")}{data.version ?? "0.0.0"}
            </span>
          </div>
          <h1 className="mt-5 text-3xl font-semibold tracking-[-0.04em] text-white sm:text-5xl">
            {translate("One foundation,")}<span className="block text-sky-300">{translate("every application.")}</span>
          </h1>
          <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-400 sm:text-base">
            {data.name ?? "Asterloom"} {translate("exposes the same contract over native gRPC and browser-ready JSON. This console is consuming the transcoded API through the Next.js BFF.")}</p>
        </div>
      </section>

      <div className="grid gap-4 md:grid-cols-3">
        <MetricCard
          detail="Native gRPC + HTTP/JSON"
          icon={Server}
          label={translate("Transport")}
          value="Dual"
        />
        <MetricCard
          detail={
            String(capabilities.length - availableCount) +
            " queued in the roadmap"
          }
          icon={Sparkles}
          label={translate("Capabilities ready")}
          value={String(availableCount) + " / " + String(capabilities.length)}
        />
        <MetricCard
          detail={serverTime}
          icon={Activity}
          label={translate("Server clock")}
          value="UTC"
        />
      </div>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{translate("Capability catalog")}</CardTitle>
            <CardDescription>
              {translate("Live lifecycle data returned by PlatformAdminService.")}</CardDescription>
          </div>
          <Button
            onClick={() => {
              toast.promise(mutate(), {
                loading: translate("Refreshing capability catalog…"),
                success: translate("Capability catalog refreshed."),
                error: translate("Refresh failed."),
              });
            }}
            size="sm"
            type="button"
            variant="outline"
          >
            <RefreshCw aria-hidden="true" className="size-3.5" />
            {translate("Refresh")}</Button>
        </CardHeader>
        <CardContent>
          <CapabilityGrid capabilities={capabilities} />
        </CardContent>
      </Card>
    </div>
  );
}

function MetricCard({
  detail,
  icon: Icon,
  label,
  value,
}: {
  detail: string;
  icon: typeof Server;
  label: string;
  value: string;
}) {
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium uppercase tracking-[0.16em] text-slate-500">
          {label}
        </p>
        <Icon aria-hidden="true" className="size-4 text-sky-400" />
      </div>
      <p className="mt-5 text-2xl font-semibold tracking-tight text-white">
        {value}
      </p>
      <p className="mt-1 truncate text-xs text-slate-500">{detail}</p>
    </Card>
  );
}

function PlatformOverviewSkeleton() {
  return (
    <div aria-label={translate("Loading platform information")} className="space-y-6">
      <div className="h-72 animate-pulse rounded-3xl border border-white/8 bg-white/[0.03]" />
      <div className="grid gap-4 md:grid-cols-3">
        {[0, 1, 2].map((item) => (
          <div
            className="h-32 animate-pulse rounded-2xl border border-white/8 bg-white/[0.025]"
            key={item}
          />
        ))}
      </div>
    </div>
  );
}
