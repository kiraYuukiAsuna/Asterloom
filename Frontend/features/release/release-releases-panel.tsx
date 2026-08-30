"use client";

import {
  CheckCircle2,
  ClipboardCheck,
  Eye,
  FlaskConical,
  History,
  LoaderCircle,
  PackageCheck,
  Pause,
  Play,
  Plus,
  Rocket,
  RotateCcw,
  Save,
  Search,
  ShieldCheck,
} from "lucide-react";
import { type FormEvent, useMemo, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  DesktopReleaseStatusObject,
  ReleaseArtifactStatusObject,
} from "@/lib/api/generated/models";
import {
  createDesktopRelease,
  getDesktopRelease,
  getReleaseChannel,
  getReleaseManifest,
  listDesktopReleases,
  listReleaseArtifacts,
  listReleaseChannels,
  listReleaseSigningKeys,
  pauseDesktopRelease,
  promoteDesktopRelease,
  publishDesktopRelease,
  releaseErrorMessage,
  rollbackDesktopRelease,
  simulateReleaseUpdate,
  updateDesktopReleaseDraft,
  validateDesktopRelease,
  type DesktopReleaseRecord,
  type ReleaseArtifactRecord,
  type ReleaseChannelRecord,
  type ReleaseDecisionRecord,
  type ReleaseManifestRecord,
  type ReleaseScope,
  type ReleaseSigningKeyRecord,
  type ReleaseValidationRecord,
} from "@/lib/api/release-management";
import { listSegments } from "@/lib/api/targeting-management";
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

const page = { pageSize: 100, pageToken: "", query: "" };

export function ReleaseReleasesPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: ReleaseScope;
}) {
  const [query, setQuery] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [selected, setSelected] = useState<DesktopReleaseRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const releases = useSWR(
    [
      "desktop-releases",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
      query,
      includeInactive,
    ],
    () => listDesktopReleases(scope, { ...page, includeInactive, query }),
    { keepPreviousData: true },
  );
  const channels = useSWR(
    ["release-form-channels", scope.tenantId, scope.applicationId, scope.environmentId],
    () => listReleaseChannels(scope, { ...page, includeArchived: false }),
    { dedupingInterval: 0, revalidateOnMount: true },
  );
  const artifacts = useSWR(
    ["release-form-artifacts", scope.tenantId, scope.applicationId, scope.environmentId],
    () => listReleaseArtifacts(scope, { ...page, includeArchived: false }),
    { dedupingInterval: 0, revalidateOnMount: true },
  );
  const signingKeys = useSWR(
    ["release-form-keys", scope.tenantId],
    () =>
      listReleaseSigningKeys(scope.tenantId, {
        ...page,
        includeArchived: false,
      }),
    { dedupingInterval: 0, revalidateOnMount: true },
  );
  const segments = useSWR(
    ["release-form-segments", scope.tenantId, scope.applicationId, scope.environmentId],
    () => listSegments(scope, { ...page, includeArchived: false }),
    { dedupingInterval: 0, revalidateOnMount: true },
  );

  const activeArtifacts = (artifacts.data?.artifacts ?? []).filter(
    (artifact) =>
      artifact.status === ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED,
  );

  async function inspect(release: DesktopReleaseRecord) {
    setGettingId(release.id);
    try {
      setSelected(await getDesktopRelease(scope, release.id));
      toast.success("Release details refreshed.");
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setGettingId("");
    }
  }

  async function changed(release: DesktopReleaseRecord) {
    setSelected(release);
    await Promise.all([releases.mutate(), channels.mutate()]);
  }

  const collectionError =
    channels.error ?? artifacts.error ?? signingKeys.error ?? segments.error;

  return (
    <div className="space-y-6">
      {collectionError && <ReleaseErrorState error={collectionError} />}
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(28rem,0.85fr)]">
        <div className="space-y-6">
          <Card data-ui-action="list-desktop-releases">
            <CardHeader>
              <CardTitle>Desktop release inventory</CardTitle>
              <CardDescription>
                Draft, publish, pause, promote, and roll back signed manifests per
                channel.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <form
                className="flex gap-3"
                onSubmit={(event) => {
                  event.preventDefault();
                  setQuery(queryDraft.trim());
                  setSelected(null);
                }}
              >
                <label className="relative flex-1">
                  <span className="sr-only">Search desktop releases</span>
                  <Search
                    aria-hidden="true"
                    className="absolute left-3 top-3 size-4 text-slate-600"
                  />
                  <input
                    className={cn(releaseInputClassName, "pl-9")}
                    name="releaseSearch"
                    onChange={(event) => setQueryDraft(event.target.value)}
                    placeholder="Search version or display name"
                    value={queryDraft}
                  />
                </label>
                <Button type="submit" variant="outline">
                  Apply
                </Button>
              </form>
              <label className="flex items-center gap-2 text-xs text-slate-400">
                <input
                  aria-label="Include inactive desktop releases"
                  checked={includeInactive}
                  onChange={(event) => setIncludeInactive(event.target.checked)}
                  type="checkbox"
                />
                Include paused and rolled-back releases
              </label>

              {releases.isLoading ? (
                <ReleaseLoadingState label="Loading releases" />
              ) : releases.error ? (
                <ReleaseErrorState error={releases.error} />
              ) : (releases.data?.releases.length ?? 0) === 0 ? (
                <ReleaseEmptyState message="No desktop releases match this view." />
              ) : (
                <div className="space-y-2">
                  {releases.data?.releases.map((release) => {
                    const channel = channels.data?.channels.find(
                      (item) => item.id === release.channelId,
                    );
                    return (
                      <article
                        className={cn(
                          "rounded-xl border p-4 transition",
                          selected?.id === release.id
                            ? "border-violet-400/30 bg-violet-400/[0.06]"
                            : "border-white/8 bg-white/[0.02] hover:border-white/15",
                        )}
                        data-testid={`desktop-release-${release.releaseVersion}-${channel?.key ?? release.channelId}`}
                        key={release.id}
                      >
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                          <button
                            className="min-w-0 text-left"
                            onClick={() => setSelected(release)}
                            type="button"
                          >
                            <span className="flex flex-wrap items-center gap-2">
                              <PackageCheck
                                aria-hidden="true"
                                className="size-3.5 text-violet-300"
                              />
                              <span className="text-sm font-medium text-white">
                                {release.displayName}
                              </span>
                              <ReleaseStatusBadge status={release.status} />
                            </span>
                            <span className="mt-1 block font-mono text-xs text-violet-300">
                              {release.releaseVersion} · {channel?.key ?? "Unknown channel"}
                            </span>
                            <span className="mt-2 block text-xs text-slate-500">
                              {(release.rolloutBasisPoints / 1_000).toFixed(2)}% rollout ·{" "}
                              {release.artifactIds.length} artifact(s) · revision{" "}
                              {release.revision}
                            </span>
                          </button>
                          <Button
                            data-ui-action="get-desktop-release"
                            disabled={gettingId === release.id}
                            onClick={() => void inspect(release)}
                            size="sm"
                            type="button"
                            variant="outline"
                          >
                            {gettingId === release.id ? (
                              <LoaderCircle
                                aria-hidden="true"
                                className="size-3.5 animate-spin"
                              />
                            ) : (
                              <Eye aria-hidden="true" className="size-3.5" />
                            )}
                            Inspect
                          </Button>
                        </div>
                      </article>
                    );
                  })}
                </div>
              )}
            </CardContent>
          </Card>

          <CreateReleaseCard
            artifacts={activeArtifacts}
            channels={channels.data?.channels ?? []}
            csrfToken={csrfToken}
            onCreated={changed}
            scope={scope}
            segments={segments.data?.segments ?? []}
          />
        </div>

        {selected ? (
          <ReleaseInspector
            allReleases={releases.data?.releases ?? []}
            artifacts={activeArtifacts}
            csrfToken={csrfToken}
            key={`${selected.id}:${selected.version}`}
            onChanged={changed}
            release={selected}
            scope={scope}
            segments={segments.data?.segments ?? []}
            signingKeys={signingKeys.data?.signingKeys ?? []}
          />
        ) : (
          <ReleaseEmptyState message="Select a release to edit, validate, sign, or control rollout." />
        )}
      </div>

      <ReleaseSimulationCard
        channels={channels.data?.channels ?? []}
        csrfToken={csrfToken}
        scope={scope}
      />
    </div>
  );
}

function CreateReleaseCard({
  artifacts,
  channels,
  csrfToken,
  onCreated,
  scope,
  segments,
}: {
  artifacts: ReleaseArtifactRecord[];
  channels: ReleaseChannelRecord[];
  csrfToken: string;
  onCreated: (release: DesktopReleaseRecord) => Promise<void>;
  scope: ReleaseScope;
  segments: Array<{ displayName: string; id: string; key: string }>;
}) {
  const [channelId, setChannelId] = useState("");
  const [releaseVersion, setReleaseVersion] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [releaseNotes, setReleaseNotes] = useState("");
  const [artifactIds, setArtifactIds] = useState<string[]>([]);
  const [rolloutBasisPoints, setRolloutBasisPoints] = useState("100000");
  const [targetSegmentId, setTargetSegmentId] = useState("");
  const [mandatory, setMandatory] = useState(false);
  const [minimumVersion, setMinimumVersion] = useState("0.0.0");
  const [busy, setBusy] = useState(false);
  const compatibleArtifacts = artifacts.filter(
    (artifact) => !releaseVersion || artifact.releaseVersion === releaseVersion.trim(),
  );

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const release = await createDesktopRelease(csrfToken, scope, {
        artifactIds,
        channelId,
        displayName,
        mandatory,
        minimumVersion,
        releaseNotes,
        releaseVersion,
        rolloutBasisPoints: Number(rolloutBasisPoints),
        targetSegmentId: targetSegmentId || null,
      });
      await onCreated(release);
      setReleaseVersion("");
      setDisplayName("");
      setReleaseNotes("");
      setArtifactIds([]);
      setTargetSegmentId("");
      toast.success("Desktop release draft created.");
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Create release draft</CardTitle>
        <CardDescription>
          Attach verified artifacts for one Semantic Version and choose the initial
          deterministic rollout.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <label className={releaseLabelClassName}>
            Channel
            <select
              className={releaseInputClassName}
              name="releaseChannel"
              onChange={(event) => setChannelId(event.target.value)}
              required
              value={channelId}
            >
              <option value="">Choose a channel</option>
              {channels.map((channel) => (
                <option key={channel.id} value={channel.id}>
                  {channel.displayName} ({channel.key})
                </option>
              ))}
            </select>
          </label>
          <label className={releaseLabelClassName}>
            Semantic Version
            <input
              className={releaseInputClassName}
              name="releaseVersion"
              onChange={(event) => {
                setReleaseVersion(event.target.value);
                setArtifactIds([]);
              }}
              placeholder="1.0.0"
              required
              value={releaseVersion}
            />
          </label>
          <label className={releaseLabelClassName}>
            Display name
            <input
              className={releaseInputClassName}
              name="releaseDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder="Asterloom Desktop 1.0"
              required
              value={displayName}
            />
          </label>
          <label className={releaseLabelClassName}>
            Minimum client version
            <input
              className={releaseInputClassName}
              name="releaseMinimumVersion"
              onChange={(event) => setMinimumVersion(event.target.value)}
              required
              value={minimumVersion}
            />
          </label>
          <label className={releaseLabelClassName}>
            Rollout basis points
            <input
              className={releaseInputClassName}
              max="100000"
              min="1"
              name="releaseRolloutBasisPoints"
              onChange={(event) => setRolloutBasisPoints(event.target.value)}
              required
              type="number"
              value={rolloutBasisPoints}
            />
          </label>
          <label className={releaseLabelClassName}>
            Target segment (optional)
            <select
              className={releaseInputClassName}
              name="releaseTargetSegment"
              onChange={(event) => setTargetSegmentId(event.target.value)}
              value={targetSegmentId}
            >
              <option value="">All eligible clients</option>
              {segments.map((segment) => (
                <option key={segment.id} value={segment.id}>
                  {segment.displayName} ({segment.key})
                </option>
              ))}
            </select>
          </label>
          <label className={cn(releaseLabelClassName, "sm:col-span-2")}>
            Release notes
            <textarea
              className={releaseTextAreaClassName}
              name="releaseNotes"
              onChange={(event) => setReleaseNotes(event.target.value)}
              value={releaseNotes}
            />
          </label>
          <fieldset className="sm:col-span-2 space-y-2 rounded-xl border border-white/8 p-4">
            <legend className="px-1 text-xs font-medium text-slate-400">
              Verified artifacts for {releaseVersion || "this version"}
            </legend>
            {compatibleArtifacts.length === 0 ? (
              <p className="text-xs text-slate-600">
                Upload and verify at least one full artifact with the same release
                version.
              </p>
            ) : (
              compatibleArtifacts.map((artifact) => (
                <label
                  className="flex items-center gap-3 rounded-lg border border-white/8 p-3 text-xs text-slate-300"
                  key={artifact.id}
                >
                  <input
                    aria-label={`Create artifact ${artifact.fileName}`}
                    checked={artifactIds.includes(artifact.id)}
                    onChange={(event) =>
                      setArtifactIds((current) =>
                        event.target.checked
                          ? [...current, artifact.id]
                          : current.filter((id) => id !== artifact.id),
                      )
                    }
                    type="checkbox"
                  />
                  <span>
                    {artifact.targetRuntimeId} ·{" "}
                    {artifact.artifactKind.endsWith("_DELTA") ? "delta" : "full"} ·{" "}
                    {artifact.fileName}
                  </span>
                </label>
              ))
            )}
          </fieldset>
          <label className="flex items-center gap-2 text-xs text-slate-400 sm:col-span-2">
            <input
              aria-label="Mandatory desktop release"
              checked={mandatory}
              onChange={(event) => setMandatory(event.target.checked)}
              type="checkbox"
            />
            Mark this update as mandatory
          </label>
          <div className="sm:col-span-2">
            <Button
              data-ui-action="create-desktop-release"
              disabled={busy || artifactIds.length === 0}
              type="submit"
            >
              {busy ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Plus aria-hidden="true" className="size-4" />
              )}
              Create draft
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function ReleaseInspector({
  allReleases,
  artifacts,
  csrfToken,
  onChanged,
  release,
  scope,
  segments,
  signingKeys,
}: {
  allReleases: DesktopReleaseRecord[];
  artifacts: ReleaseArtifactRecord[];
  csrfToken: string;
  onChanged: (release: DesktopReleaseRecord) => Promise<void>;
  release: DesktopReleaseRecord;
  scope: ReleaseScope;
  segments: Array<{ displayName: string; id: string; key: string }>;
  signingKeys: ReleaseSigningKeyRecord[];
}) {
  const [displayName, setDisplayName] = useState(release.displayName);
  const [releaseNotes, setReleaseNotes] = useState(release.releaseNotes);
  const [artifactIds, setArtifactIds] = useState([...release.artifactIds]);
  const [rolloutBasisPoints, setRolloutBasisPoints] = useState(
    String(release.rolloutBasisPoints),
  );
  const [targetSegmentId, setTargetSegmentId] = useState(
    release.targetSegmentId ?? "",
  );
  const [mandatory, setMandatory] = useState(release.mandatory);
  const [minimumVersion, setMinimumVersion] = useState(release.minimumVersion);
  const [validation, setValidation] = useState<ReleaseValidationRecord | null>(null);
  const [manifest, setManifest] = useState<ReleaseManifestRecord | null>(null);
  const [manifestSigningKeyId, setManifestSigningKeyId] = useState("");
  const [manifestSignature, setManifestSignature] = useState("");
  const [promotionBasisPoints, setPromotionBasisPoints] = useState(
    String(Math.min(100_000, Math.max(release.rolloutBasisPoints + 1, 100_000))),
  );
  const [rollbackTargetId, setRollbackTargetId] = useState("");
  const [busy, setBusy] = useState("");
  const draft = release.status === DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_DRAFT;
  const published =
    release.status === DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_PUBLISHED;
  const paused = release.status === DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_PAUSED;
  const compatibleArtifacts = artifacts.filter(
    (artifact) => artifact.releaseVersion === release.releaseVersion,
  );
  const rollbackTargets = allReleases.filter(
    (candidate) =>
      candidate.id !== release.id &&
      candidate.channelId === release.channelId &&
      candidate.status !== DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_DRAFT &&
      Boolean(candidate.manifestSha256),
  );

  async function perform(
    operation: string,
    action: () => Promise<DesktopReleaseRecord>,
    success: string,
  ) {
    setBusy(operation);
    try {
      const updated = await action();
      await onChanged(updated);
      toast.success(success);
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setBusy("");
    }
  }

  async function validate() {
    setBusy("validate");
    try {
      const result = await validateDesktopRelease(csrfToken, release);
      setValidation(result);
      toast[result.valid ? "success" : "error"](
        result.valid ? "Release candidate is valid." : "Release validation found errors.",
      );
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setBusy("");
    }
  }

  async function publish() {
    await perform(
      "publish",
      async () => {
        const channel = await getReleaseChannel(scope, release.channelId);
        return publishDesktopRelease(
          csrfToken,
          release,
          channel,
          manifestSigningKeyId,
          manifestSignature,
        );
      },
      "Signed release published and channel activated.",
    );
  }

  async function loadManifest() {
    setBusy("manifest");
    try {
      setManifest(await getReleaseManifest(release));
      toast.success("Signed release manifest loaded.");
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setBusy("");
    }
  }

  async function rollback() {
    const target = rollbackTargets.find((candidate) => candidate.id === rollbackTargetId);
    if (!target) {
      toast.error("Choose a previously signed release in this channel.");
      return;
    }
    await perform(
      "rollback",
      async () => {
        const [currentRelease, targetRelease, channel] = await Promise.all([
          getDesktopRelease(scope, release.id),
          getDesktopRelease(scope, target.id),
          getReleaseChannel(scope, release.channelId),
        ]);
        return rollbackDesktopRelease(
          csrfToken,
          currentRelease,
          targetRelease,
          channel,
        );
      },
      "Release rolled back and previous signed manifest reactivated.",
    );
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>{release.displayName}</CardTitle>
          <ReleaseStatusBadge status={release.status} />
        </div>
        <CardDescription className="font-mono">
          {release.releaseVersion} · revision {release.revision}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        <section className="space-y-4">
          <h3 className="text-xs font-semibold uppercase tracking-[0.13em] text-slate-500">
            Draft definition
          </h3>
          <label className={releaseLabelClassName}>
            Display name
            <input
              className={releaseInputClassName}
              disabled={!draft}
              name="editReleaseDisplayName"
              onChange={(event) => setDisplayName(event.target.value)}
              value={displayName}
            />
          </label>
          <label className={releaseLabelClassName}>
            Release notes
            <textarea
              className={releaseTextAreaClassName}
              disabled={!draft}
              name="editReleaseNotes"
              onChange={(event) => setReleaseNotes(event.target.value)}
              value={releaseNotes}
            />
          </label>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className={releaseLabelClassName}>
              Rollout basis points
              <input
                className={releaseInputClassName}
                disabled={!draft}
                max="100000"
                min="1"
                name="editReleaseRolloutBasisPoints"
                onChange={(event) => setRolloutBasisPoints(event.target.value)}
                type="number"
                value={rolloutBasisPoints}
              />
            </label>
            <label className={releaseLabelClassName}>
              Minimum version
              <input
                className={releaseInputClassName}
                disabled={!draft}
                name="editReleaseMinimumVersion"
                onChange={(event) => setMinimumVersion(event.target.value)}
                value={minimumVersion}
              />
            </label>
          </div>
          <label className={releaseLabelClassName}>
            Target segment
            <select
              className={releaseInputClassName}
              disabled={!draft}
              name="editReleaseTargetSegment"
              onChange={(event) => setTargetSegmentId(event.target.value)}
              value={targetSegmentId}
            >
              <option value="">All eligible clients</option>
              {segments.map((segment) => (
                <option key={segment.id} value={segment.id}>
                  {segment.displayName} ({segment.key})
                </option>
              ))}
            </select>
          </label>
          <fieldset className="space-y-2 rounded-xl border border-white/8 p-4">
            <legend className="px-1 text-xs font-medium text-slate-400">
              Release artifacts
            </legend>
            {compatibleArtifacts.map((artifact) => (
              <label className="flex items-center gap-2 text-xs text-slate-300" key={artifact.id}>
                <input
                  aria-label={`Edit artifact ${artifact.fileName}`}
                  checked={artifactIds.includes(artifact.id)}
                  disabled={!draft}
                  onChange={(event) =>
                    setArtifactIds((current) =>
                      event.target.checked
                        ? [...current, artifact.id]
                        : current.filter((id) => id !== artifact.id),
                    )
                  }
                  type="checkbox"
                />
                {artifact.targetRuntimeId} · {artifact.fileName}
              </label>
            ))}
          </fieldset>
          <label className="flex items-center gap-2 text-xs text-slate-400">
            <input
              aria-label="Edit mandatory desktop release"
              checked={mandatory}
              disabled={!draft}
              onChange={(event) => setMandatory(event.target.checked)}
              type="checkbox"
            />
            Mandatory update
          </label>
          {draft && (
            <Button
              data-ui-action="update-desktop-release-draft"
              disabled={Boolean(busy)}
              onClick={() =>
                void perform(
                  "update",
                  () =>
                    updateDesktopReleaseDraft(csrfToken, release, {
                      artifactIds,
                      displayName,
                      mandatory,
                      minimumVersion,
                      releaseNotes,
                      rolloutBasisPoints: Number(rolloutBasisPoints),
                      targetSegmentId: targetSegmentId || null,
                    }),
                  "Release draft updated.",
                )
              }
              type="button"
            >
              {busy === "update" ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <Save aria-hidden="true" className="size-4" />
              )}
              Save draft
            </Button>
          )}
        </section>

        <section className="space-y-4 border-t border-white/8 pt-5">
          <h3 className="text-xs font-semibold uppercase tracking-[0.13em] text-slate-500">
            Validation and signed manifest
          </h3>
          <Button
            data-ui-action="validate-desktop-release"
            disabled={Boolean(busy)}
            onClick={() => void validate()}
            type="button"
            variant="outline"
          >
            <ClipboardCheck aria-hidden="true" className="size-4" />
            Validate release
          </Button>
          {validation && (
            <div
              className={cn(
                "rounded-xl border p-4 text-xs",
                validation.valid
                  ? "border-emerald-400/20 bg-emerald-400/[0.06] text-emerald-200"
                  : "border-rose-400/20 bg-rose-400/[0.06] text-rose-200",
              )}
            >
              <p className="flex items-center gap-2 font-medium">
                {validation.valid ? (
                  <CheckCircle2 aria-hidden="true" className="size-4" />
                ) : (
                  <ShieldCheck aria-hidden="true" className="size-4" />
                )}
                {validation.valid ? "Candidate manifest is valid" : "Validation failed"}
              </p>
              {validation.candidateManifest && (
                <output
                  className="mt-3 block break-all font-mono text-[11px] text-violet-300"
                  data-testid="manifest-sha256"
                >
                  {validation.candidateManifest.sha256}
                </output>
              )}
              {validation.issues.length > 0 && (
                <ul className="mt-3 space-y-1">
                  {validation.issues.map((issue) => (
                    <li key={`${issue.code}:${issue.path}`}>
                      {issue.code}: {issue.message}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {draft && (
            <div className="space-y-4 rounded-xl border border-violet-400/15 bg-violet-400/[0.04] p-4">
              <p className="text-xs leading-5 text-slate-400">
                Sign the validated manifest SHA-256 text with RSA-PSS/SHA-256 in your
                external signer. Validation must be rerun after every draft change.
              </p>
              <label className={releaseLabelClassName}>
                Manifest signing key
                <select
                  className={releaseInputClassName}
                  name="manifestSigningKey"
                  onChange={(event) => setManifestSigningKeyId(event.target.value)}
                  value={manifestSigningKeyId}
                >
                  <option value="">Choose a key</option>
                  {signingKeys.map((key) => (
                    <option key={key.id} value={key.id}>
                      {key.displayName} ({key.key})
                    </option>
                  ))}
                </select>
              </label>
              <label className={releaseLabelClassName}>
                Detached manifest signature
                <textarea
                  className={cn(releaseTextAreaClassName, "font-mono text-xs")}
                  name="manifestSignature"
                  onChange={(event) => setManifestSignature(event.target.value)}
                  value={manifestSignature}
                />
              </label>
              <Button
                data-ui-action="publish-desktop-release"
                disabled={
                  Boolean(busy) ||
                  !validation?.valid ||
                  !manifestSigningKeyId ||
                  !manifestSignature
                }
                onClick={() => void publish()}
                type="button"
              >
                {busy === "publish" ? (
                  <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                ) : (
                  <Rocket aria-hidden="true" className="size-4" />
                )}
                Publish signed release
              </Button>
            </div>
          )}

          {!draft && (
            <>
              <Button
                data-ui-action="get-release-manifest"
                disabled={Boolean(busy)}
                onClick={() => void loadManifest()}
                type="button"
                variant="outline"
              >
                <Eye aria-hidden="true" className="size-4" />
                View signed manifest
              </Button>
              {manifest && (
                <div className="rounded-xl border border-white/8 bg-slate-950/70 p-4 text-xs">
                  <p className="font-medium text-white">
                    {manifest.channelKey}/{manifest.releaseVersion}
                  </p>
                  <p className="mt-2 break-all font-mono text-violet-300">
                    {manifest.sha256}
                  </p>
                  <pre className="mt-3 max-h-56 overflow-auto whitespace-pre-wrap break-all text-[10px] leading-5 text-slate-500">
                    {manifest.payloadJson}
                  </pre>
                </div>
              )}
            </>
          )}
        </section>

        {(published || paused) && (
          <section className="space-y-4 border-t border-white/8 pt-5">
            <h3 className="text-xs font-semibold uppercase tracking-[0.13em] text-slate-500">
              Rollout controls
            </h3>
            <div className="flex flex-wrap gap-2">
              {published && (
                <Button
                  data-ui-action="pause-desktop-release"
                  disabled={Boolean(busy)}
                  onClick={() =>
                    void perform(
                      "pause",
                      () => pauseDesktopRelease(csrfToken, release),
                      "Active release paused.",
                    )
                  }
                  type="button"
                  variant="outline"
                >
                  <Pause aria-hidden="true" className="size-4" />
                  Pause
                </Button>
              )}
            </div>
            <div className="flex gap-2">
              <input
                aria-label="Promotion rollout basis points"
                className={releaseInputClassName}
                max="100000"
                min={paused ? release.rolloutBasisPoints : release.rolloutBasisPoints + 1}
                name="promotionRolloutBasisPoints"
                onChange={(event) => setPromotionBasisPoints(event.target.value)}
                type="number"
                value={promotionBasisPoints}
              />
              <Button
                data-ui-action="promote-desktop-release"
                disabled={Boolean(busy)}
                onClick={() =>
                  void perform(
                    "promote",
                    () =>
                      promoteDesktopRelease(
                        csrfToken,
                        release,
                        Number(promotionBasisPoints),
                      ),
                    paused ? "Release resumed and rollout updated." : "Rollout promoted.",
                  )
                }
                type="button"
              >
                <Play aria-hidden="true" className="size-4" />
                {paused ? "Resume" : "Promote"}
              </Button>
            </div>
            <div className="space-y-2 rounded-xl border border-white/8 p-4">
              <label className={releaseLabelClassName}>
                Rollback target
                <select
                  className={releaseInputClassName}
                  name="rollbackTargetRelease"
                  onChange={(event) => setRollbackTargetId(event.target.value)}
                  value={rollbackTargetId}
                >
                  <option value="">Choose a previous signed release</option>
                  {rollbackTargets.map((target) => (
                    <option key={target.id} value={target.id}>
                      {target.releaseVersion} · {target.displayName}
                    </option>
                  ))}
                </select>
              </label>
              <Button
                data-ui-action="rollback-desktop-release"
                disabled={Boolean(busy) || !rollbackTargetId}
                onClick={() => void rollback()}
                type="button"
                variant="outline"
              >
                <RotateCcw aria-hidden="true" className="size-4" />
                Roll back active release
              </Button>
            </div>
          </section>
        )}
      </CardContent>
    </Card>
  );
}

function ReleaseSimulationCard({
  channels,
  csrfToken,
  scope,
}: {
  channels: ReleaseChannelRecord[];
  csrfToken: string;
  scope: ReleaseScope;
}) {
  const [channelKey, setChannelKey] = useState("");
  const [currentVersion, setCurrentVersion] = useState("0.0.0");
  const [targetRuntimeId, setTargetRuntimeId] = useState("win-x64");
  const [targetingKey, setTargetingKey] = useState("preview-user");
  const [userId, setUserId] = useState("");
  const [clientVersion, setClientVersion] = useState("");
  const [region, setRegion] = useState("");
  const [decision, setDecision] = useState<ReleaseDecisionRecord | null>(null);
  const [busy, setBusy] = useState(false);
  const activeChannelKeys = useMemo(
    () => channels.map((channel) => channel.key).sort(),
    [channels],
  );

  async function simulate(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const result = await simulateReleaseUpdate(csrfToken, scope, {
        channelKey,
        clientVersion,
        currentVersion,
        region,
        targetRuntimeId,
        targetingKey,
        userId,
      });
      setDecision(result);
      toast.success("Update decision simulated.");
    } catch (error) {
      toast.error(releaseErrorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Update decision simulator</CardTitle>
        <CardDescription>
          Exercise channel state, Semantic Version comparison, targeting, stable
          bucketing, and compatible artifact selection without changing rollout state.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(22rem,0.75fr)]">
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={simulate}>
          <label className={releaseLabelClassName}>
            Channel key
            <input
              className={releaseInputClassName}
              list="release-channel-keys"
              name="simulationChannelKey"
              onChange={(event) => setChannelKey(event.target.value)}
              placeholder={activeChannelKeys[0] ?? "stable"}
              required
              value={channelKey}
            />
            <datalist id="release-channel-keys">
              {activeChannelKeys.map((key) => (
                <option key={key} value={key} />
              ))}
            </datalist>
          </label>
          <label className={releaseLabelClassName}>
            Current version
            <input
              className={releaseInputClassName}
              name="simulationCurrentVersion"
              onChange={(event) => setCurrentVersion(event.target.value)}
              required
              value={currentVersion}
            />
          </label>
          <label className={releaseLabelClassName}>
            Target runtime
            <input
              className={releaseInputClassName}
              name="simulationRuntimeId"
              onChange={(event) => setTargetRuntimeId(event.target.value)}
              required
              value={targetRuntimeId}
            />
          </label>
          <label className={releaseLabelClassName}>
            Targeting key
            <input
              className={releaseInputClassName}
              name="simulationTargetingKey"
              onChange={(event) => setTargetingKey(event.target.value)}
              required
              value={targetingKey}
            />
          </label>
          <label className={releaseLabelClassName}>
            User ID
            <input
              className={releaseInputClassName}
              name="simulationUserId"
              onChange={(event) => setUserId(event.target.value)}
              value={userId}
            />
          </label>
          <label className={releaseLabelClassName}>
            Client version attribute
            <input
              className={releaseInputClassName}
              name="simulationClientVersion"
              onChange={(event) => setClientVersion(event.target.value)}
              value={clientVersion}
            />
          </label>
          <label className={releaseLabelClassName}>
            Region
            <input
              className={releaseInputClassName}
              name="simulationRegion"
              onChange={(event) => setRegion(event.target.value)}
              value={region}
            />
          </label>
          <div className="flex items-end">
            <Button
              data-ui-action="simulate-release-update"
              disabled={busy}
              type="submit"
            >
              {busy ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <FlaskConical aria-hidden="true" className="size-4" />
              )}
              Simulate update
            </Button>
          </div>
        </form>

        {decision ? (
          <div className="rounded-xl border border-white/8 bg-slate-950/65 p-5">
            <p className="flex items-center gap-2 text-sm font-medium text-white">
              {decision.updateAvailable ? (
                <Rocket aria-hidden="true" className="size-4 text-emerald-300" />
              ) : (
                <History aria-hidden="true" className="size-4 text-slate-500" />
              )}
              {decision.updateAvailable ? "Update available" : "No update"}
            </p>
            <p className="mt-2 font-mono text-xs text-violet-300">{decision.reason}</p>
            <dl className="mt-4 grid grid-cols-2 gap-3 text-xs">
              <div>
                <dt className="text-slate-600">Bucket</dt>
                <dd className="mt-1 text-slate-300">
                  {decision.bucketEvaluated ? decision.bucket : "Not evaluated"}
                </dd>
              </div>
              <div>
                <dt className="text-slate-600">Rollout</dt>
                <dd className="mt-1 text-slate-300">
                  {(decision.rolloutBasisPoints / 1_000).toFixed(2)}%
                </dd>
              </div>
              <div className="col-span-2">
                <dt className="text-slate-600">Selected artifact</dt>
                <dd className="mt-1 text-slate-300">
                  {decision.selectedArtifact?.fileName ?? "None"}
                </dd>
              </div>
            </dl>
            {decision.trace.length > 0 && (
              <ol className="mt-4 space-y-1 border-t border-white/8 pt-4 font-mono text-[10px] text-slate-500">
                {decision.trace.map((item, index) => (
                  <li key={`${index}:${item}`}>{item}</li>
                ))}
              </ol>
            )}
          </div>
        ) : (
          <div className="grid min-h-48 place-items-center rounded-xl border border-dashed border-white/10 text-sm text-slate-600">
            Run a simulation to inspect the decision trace.
          </div>
        )}
      </CardContent>
    </Card>
  );
}
