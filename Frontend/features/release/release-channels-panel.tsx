"use client";

import {
  Archive,
  Eye,
  LoaderCircle,
  Plus,
  Radio,
  RotateCcw,
  Save,
  Search,
} from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR, { useSWRConfig } from "swr";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { ReleaseChannelStatusObject } from "@/lib/api/generated/models";
import {
  archiveReleaseChannel,
  createReleaseChannel,
  getReleaseChannel,
  listReleaseChannels,
  releaseErrorMessage,
  restoreReleaseChannel,
  updateReleaseChannel,
  type ReleaseChannelRecord,
  type ReleaseScope,
} from "@/lib/api/release-management";
import { cn } from "@/lib/utils/cn";

import {
  releaseInputClassName,
  releaseLabelClassName,
  releaseTextAreaClassName,
  ReleaseEmptyState,
  ReleaseErrorState,
  ReleaseLoadingState,
  ReleaseStatusBadge,
} from "./release-ui";
import { translate } from "@/lib/i18n/locale";

export function ReleaseChannelsPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: ReleaseScope;
}) {
  const [query, setQuery] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [selected, setSelected] = useState<ReleaseChannelRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const { mutate: mutateGlobal } = useSWRConfig();
  const channels = useSWR(
    [
      "release-channels",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
      query,
      includeArchived,
    ],
    () =>
      listReleaseChannels(scope, {
        includeArchived,
        pageSize: 100,
        pageToken: "",
        query,
      }),
    { keepPreviousData: true },
  );

  function applySearch(event: FormEvent) {
    event.preventDefault();
    setQuery(queryDraft.trim());
    setSelected(null);
  }

  async function inspect(channel: ReleaseChannelRecord) {
    setGettingId(channel.id);
    try {
      setSelected(await getReleaseChannel(scope, channel.id));
      toast.success(translate("Channel details refreshed."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  async function changed(channel: ReleaseChannelRecord) {
    setSelected(channel);
    await channels.mutate();
    await mutateGlobal(
      [
        "release-form-channels",
        scope.tenantId,
        scope.applicationId,
        scope.environmentId,
      ],
      undefined,
      { revalidate: false },
    );
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
      <div className="space-y-6">
        <Card data-ui-action="list-release-channels">
          <CardHeader>
            <CardTitle>{translate("Channel inventory")}</CardTitle>
            <CardDescription>
              {translate("Stable channel keys route desktop clients to one active signed release.")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <form className="flex gap-3" onSubmit={applySearch}>
              <label className="relative flex-1">
                <span className="sr-only">{translate("Search release channels")}</span>
                <Search
                  aria-hidden="true"
                  className="absolute left-3 top-3 size-4 text-slate-600"
                />
                <input
                  className={cn(releaseInputClassName, "pl-9")}
                  name="channelSearch"
                  onChange={(event) => setQueryDraft(event.target.value)}
                  placeholder={translate("Search channel key or name")}
                  value={queryDraft}
                />
              </label>
              <Button type="submit" variant="outline">
                {translate("Apply")}</Button>
            </form>
            <label className="flex items-center gap-2 text-xs text-slate-400">
              <input
                aria-label={translate("Include archived release channels")}
                checked={includeArchived}
                onChange={(event) => setIncludeArchived(event.target.checked)}
                type="checkbox"
              />
              {translate("Include archived channels")}</label>

            {channels.isLoading ? (
              <ReleaseLoadingState label={translate("Loading channels")} />
            ) : channels.error ? (
              <ReleaseErrorState error={channels.error} />
            ) : (channels.data?.channels.length ?? 0) === 0 ? (
              <ReleaseEmptyState message={translate("No release channels match this view.")} />
            ) : (
              <div className="space-y-2">
                {channels.data?.channels.map((channel) => (
                  <article
                    className={cn(
                      "rounded-xl border p-4 transition",
                      selected?.id === channel.id
                        ? "border-violet-400/30 bg-violet-400/[0.06]"
                        : "border-white/8 bg-white/[0.02] hover:border-white/15",
                    )}
                    data-testid={`release-channel-${channel.key}`}
                    key={channel.id}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <button
                        className="min-w-0 text-left"
                        onClick={() => setSelected(channel)}
                        type="button"
                      >
                        <span className="flex items-center gap-2">
                          <Radio aria-hidden="true" className="size-3.5 text-violet-300" />
                          <span className="truncate text-sm font-medium text-white">
                            {channel.displayName}
                          </span>
                          <ReleaseStatusBadge status={channel.status} />
                        </span>
                        <span className="mt-1 block font-mono text-xs text-violet-300">
                          {channel.key}
                        </span>
                        <span className="mt-2 block text-xs text-slate-500">
                          {translate(channel.activeReleaseId
                            ? `Active release ${channel.activeReleaseId.slice(0, 8)}`
                            : "No active release")}
                        </span>
                      </button>
                      <Button
                        data-ui-action="get-release-channel"
                        disabled={gettingId === channel.id}
                        onClick={() => void inspect(channel)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {gettingId === channel.id ? (
                          <LoaderCircle aria-hidden="true" className="size-3.5 animate-spin" />
                        ) : (
                          <Eye aria-hidden="true" className="size-3.5" />
                        )}
                        {translate("Inspect")}</Button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <CreateChannelCard
          csrfToken={csrfToken}
          onCreated={changed}
          scope={scope}
        />
      </div>

      {selected ? (
        <ChannelInspector
          channel={selected}
          csrfToken={csrfToken}
          key={`${selected.id}:${selected.version}`}
          onChanged={changed}
        />
      ) : (
        <ReleaseEmptyState message={translate("Select a channel to edit its description or lifecycle.")} />
      )}
    </div>
  );
}

function CreateChannelCard({
  csrfToken,
  onCreated,
  scope,
}: {
  csrfToken: string;
  onCreated: (channel: ReleaseChannelRecord) => Promise<void>;
  scope: ReleaseScope;
}) {
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const channel = await createReleaseChannel(csrfToken, scope, {
        description,
        displayName,
        key,
      });
      await onCreated(channel);
      setKey("");
      setDisplayName("");
      setDescription("");
      toast.success(translate("Release channel created."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Create channel")}</CardTitle>
        <CardDescription>
          {translate("Channel keys are immutable client-facing routing identifiers.")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <label className={releaseLabelClassName}>
            {translate("Channel key")}<input
              className={releaseInputClassName}
              name="channelKey"
              onChange={(event) => setKey(event.target.value)}
              placeholder={translate("stable")}
              required
              value={key}
            />
          </label>
          <label className={releaseLabelClassName}>
            {translate("Display name")}<input
              className={releaseInputClassName}
              name="channelDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder={translate("Stable")}
              required
              value={displayName}
            />
          </label>
          <label className={cn(releaseLabelClassName, "sm:col-span-2")}>
            {translate("Description")}<textarea
              className={releaseTextAreaClassName}
              name="channelDescription"
              onChange={(event) => setDescription(event.target.value)}
              value={description}
            />
          </label>
          <div className="sm:col-span-2">
            <Button
              data-ui-action="create-release-channel"
              disabled={busy}
              type="submit"
            >
              {busy ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Plus aria-hidden="true" className="size-4" />
              )}
              {translate("Create channel")}</Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function ChannelInspector({
  channel,
  csrfToken,
  onChanged,
}: {
  channel: ReleaseChannelRecord;
  csrfToken: string;
  onChanged: (channel: ReleaseChannelRecord) => Promise<void>;
}) {
  const [displayName, setDisplayName] = useState(channel.displayName);
  const [description, setDescription] = useState(channel.description);
  const [busy, setBusy] = useState("");
  const active =
    channel.status === ReleaseChannelStatusObject.RELEASE_CHANNEL_STATUS_ACTIVE;

  async function perform(
    operation: string,
    action: () => Promise<ReleaseChannelRecord>,
    success: string,
  ) {
    setBusy(operation);
    try {
      const updated = await action();
      await onChanged(updated);
      toast.success(translate(success));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>{channel.displayName}</CardTitle>
          <ReleaseStatusBadge status={channel.status} />
        </div>
        <CardDescription>
          <span className="block font-mono">{channel.key}</span>
          <span className="mt-1 block break-all font-mono text-[10px] text-slate-600">{channel.id}</span>
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <dl className="grid grid-cols-2 gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-xs">
          <div>
            <dt className="text-slate-600">{translate("Version")}</dt>
            <dd className="mt-1 text-slate-300">{channel.version}</dd>
          </div>
          <div>
            <dt className="text-slate-600">{translate("Active release")}</dt>
            <dd className="mt-1 truncate font-mono text-slate-300">
              {channel.activeReleaseId ?? "None"}
            </dd>
          </div>
        </dl>
        <label className={releaseLabelClassName}>
          {translate("Display name")}<input
            className={releaseInputClassName}
            disabled={!active}
            name="editChannelDisplayName"
            onChange={(event) => setDisplayName(event.target.value)}
            value={displayName}
          />
        </label>
        <label className={releaseLabelClassName}>
          {translate("Description")}<textarea
            className={releaseTextAreaClassName}
            disabled={!active}
            name="editChannelDescription"
            onChange={(event) => setDescription(event.target.value)}
            value={description}
          />
        </label>
        {active ? (
          <div className="flex flex-wrap gap-2">
            <Button
              data-ui-action="update-release-channel"
              disabled={Boolean(busy)}
              onClick={() =>
                void perform(
                  "update",
                  () =>
                    updateReleaseChannel(csrfToken, channel, {
                      description,
                      displayName,
                    }),
                  "Release channel updated.",
                )
              }
              type="button"
            >
              {busy === "update" ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Save aria-hidden="true" className="size-4" />
              )}
              {translate("Save")}</Button>
            <Button
              data-ui-action="archive-release-channel"
              disabled={Boolean(busy) || Boolean(channel.activeReleaseId)}
              onClick={() =>
                void perform(
                  "archive",
                  () => archiveReleaseChannel(csrfToken, channel),
                  "Release channel archived.",
                )
              }
              title={
                channel.activeReleaseId
                  ? "A channel with an active release cannot be archived."
                  : undefined
              }
              type="button"
              variant="outline"
            >
              <Archive aria-hidden="true" className="size-4" />
              {translate("Archive")}</Button>
          </div>
        ) : (
          <Button
            data-ui-action="restore-release-channel"
            disabled={Boolean(busy)}
            onClick={() =>
              void perform(
                "restore",
                () => restoreReleaseChannel(csrfToken, channel),
                "Release channel restored.",
              )
            }
            type="button"
          >
            <RotateCcw aria-hidden="true" className="size-4" />
            {translate("Restore channel")}</Button>
        )}
      </CardContent>
    </Card>
  );
}
