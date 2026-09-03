"use client";

import {
  Archive,
  CheckCircle2,
  Eye,
  FileKey2,
  Fingerprint,
  KeyRound,
  LoaderCircle,
  PackagePlus,
  RotateCcw,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Upload,
} from "lucide-react";
import { type ChangeEvent, type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR, { useSWRConfig } from "swr";

import { Button } from "@/components/ui/button";
import { SearchableSelect } from "@/components/ui/searchable-select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  ReleaseArtifactKindObject,
  ReleaseArtifactStatusObject,
  ReleaseSigningKeyStatusObject,
} from "@/lib/api/generated/models";
import {
  archiveReleaseArtifact,
  archiveReleaseSigningKey,
  completeReleaseArtifactUpload,
  createReleaseArtifactUpload,
  createReleaseSigningKey,
  getReleaseArtifact,
  listReleaseArtifacts,
  listReleaseSigningKeys,
  releaseErrorMessage,
  restoreReleaseSigningKey,
  uploadReleaseArtifactTransfer,
  type ReleaseArtifactKind,
  type ReleaseArtifactRecord,
  type ReleaseArtifactUploadRecord,
  type ReleaseScope,
  type ReleaseSigningKeyRecord,
} from "@/lib/api/release-management";
import { sha256Hex } from "@/lib/api/storage-management";
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
import { formatNumber } from "@/lib/i18n/format";
import { VelopackQuickUploadCard } from "./release-velopack-upload";

export function ReleaseArtifactsPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: ReleaseScope;
}) {
  return (
    <div className="space-y-6">
      <SigningKeysPanel csrfToken={csrfToken} tenantId={scope.tenantId} />
      <ArtifactsPanel csrfToken={csrfToken} scope={scope} />
    </div>
  );
}

function SigningKeysPanel({
  csrfToken,
  tenantId,
}: {
  csrfToken: string;
  tenantId: string;
}) {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [publicKeyPem, setPublicKeyPem] = useState("");
  const [busy, setBusy] = useState("");
  const { mutate: mutateGlobal } = useSWRConfig();
  const keys = useSWR(
    ["release-signing-keys", tenantId, includeArchived],
    () =>
      listReleaseSigningKeys(tenantId, {
        includeArchived,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );

  async function create(event: FormEvent) {
    event.preventDefault();
    setBusy("create");
    try {
      await createReleaseSigningKey(csrfToken, tenantId, {
        displayName,
        key,
        publicKeyPem,
      });
      await keys.mutate();
      await mutateGlobal(["release-upload-keys", tenantId]);
      await mutateGlobal(["release-form-keys", tenantId], undefined, {
        revalidate: false,
      });
      setKey("");
      setDisplayName("");
      setPublicKeyPem("");
      toast.success(translate("Release signing public key registered."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function change(
    operation: string,
    action: () => Promise<ReleaseSigningKeyRecord>,
    success: string,
  ) {
    setBusy(operation);
    try {
      await action();
      await keys.mutate();
      await mutateGlobal(["release-upload-keys", tenantId]);
      await mutateGlobal(["release-form-keys", tenantId], undefined, {
        revalidate: false,
      });
      toast.success(translate(success));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card>
      <CardHeader className="sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle>{translate("Signing trust store")}</CardTitle>
          <CardDescription>
            {translate("Register public RSA keys only. Private key material remains in your external signer or HSM.")}</CardDescription>
        </div>
        <label className="flex items-center gap-2 text-xs text-slate-400">
          <input
            aria-label={translate("Include archived release signing keys")}
            checked={includeArchived}
            onChange={(event) => setIncludeArchived(event.target.checked)}
            type="checkbox"
          />
          {translate("Include archived")}</label>
      </CardHeader>
      <CardContent className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(24rem,1fr)]">
        <div data-ui-action="list-release-signing-keys">
          {keys.isLoading ? (
            <ReleaseLoadingState label={translate("Loading signing keys")} />
          ) : keys.error ? (
            <ReleaseErrorState error={keys.error} />
          ) : (keys.data?.signingKeys.length ?? 0) === 0 ? (
            <ReleaseEmptyState message={translate("No release signing keys are registered.")} />
          ) : (
            <div className="space-y-2">
              {keys.data?.signingKeys.map((signingKey) => {
                const active =
                  signingKey.status ===
                  ReleaseSigningKeyStatusObject.RELEASE_SIGNING_KEY_STATUS_ACTIVE;
                return (
                  <article
                    className="rounded-xl border border-white/8 bg-white/[0.02] p-4"
                    data-testid={`release-signing-key-${signingKey.key}`}
                    key={signingKey.id}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div className="min-w-0">
                        <span className="flex items-center gap-2">
                          <KeyRound aria-hidden="true" className="size-3.5 text-violet-300" />
                          <span className="truncate text-sm font-medium text-white">
                            {signingKey.displayName}
                          </span>
                          <ReleaseStatusBadge status={signingKey.status} />
                        </span>
                        <p className="mt-1 font-mono text-xs text-violet-300">
                          {signingKey.key}
                        </p>
                        <p className="mt-2 truncate font-mono text-[11px] text-slate-600">
                          {signingKey.fingerprint}
                        </p>
                        <p className="mt-1 break-all font-mono text-[10px] text-slate-700">{signingKey.id}</p>
                      </div>
                      {active ? (
                        <Button
                          data-ui-action="archive-release-signing-key"
                          disabled={Boolean(busy)}
                          onClick={() =>
                            void change(
                              `archive:${signingKey.id}`,
                              () => archiveReleaseSigningKey(csrfToken, signingKey),
                              "Signing key archived.",
                            )
                          }
                          size="sm"
                          type="button"
                          variant="outline"
                        >
                          <Archive aria-hidden="true" className="size-3.5" />
                          {translate("Archive")}</Button>
                      ) : (
                        <Button
                          data-ui-action="restore-release-signing-key"
                          disabled={Boolean(busy)}
                          onClick={() =>
                            void change(
                              `restore:${signingKey.id}`,
                              () => restoreReleaseSigningKey(csrfToken, signingKey),
                              "Signing key restored.",
                            )
                          }
                          size="sm"
                          type="button"
                        >
                          <RotateCcw aria-hidden="true" className="size-3.5" />
                          {translate("Restore")}</Button>
                      )}
                    </div>
                  </article>
                );
              })}
            </div>
          )}
        </div>

        <form className="space-y-4" onSubmit={create}>
          <div>
            <h3 className="text-sm font-medium text-white">{translate("Register public key")}</h3>
            <p className="mt-1 text-xs leading-5 text-slate-500">
              {translate("RSA 2048-bit or stronger, SubjectPublicKeyInfo PEM. Algorithm: RSA-PSS-SHA256.")}</p>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className={releaseLabelClassName}>
              {translate("Key")}<input
                className={releaseInputClassName}
                name="signingKey"
                onChange={(event) => setKey(event.target.value)}
                placeholder={translate("desktop-production")}
                required
                value={key}
              />
            </label>
            <label className={releaseLabelClassName}>
              {translate("Display name")}<input
                className={releaseInputClassName}
                name="signingKeyDisplayName"
                onChange={(event) => setDisplayName(event.target.value)}
                placeholder={translate("Desktop production")}
                required
                value={displayName}
              />
            </label>
          </div>
          <label className={releaseLabelClassName}>
            {translate("Public key PEM")}<textarea
              className={cn(releaseTextAreaClassName, "h-44 font-mono text-xs")}
              name="publicKeyPem"
              onChange={(event) => setPublicKeyPem(event.target.value)}
              placeholder={translate("-----BEGIN PUBLIC KEY-----")}
              required
              value={publicKeyPem}
            />
          </label>
          <Button
            data-ui-action="create-release-signing-key"
            disabled={Boolean(busy)}
            type="submit"
          >
            {busy === "create" ? (
              <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
            ) : (
              <Fingerprint aria-hidden="true" className="size-4" />
            )}
            {translate("Register public key")}</Button>
        </form>
      </CardContent>
    </Card>
  );
}

function ArtifactsPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: ReleaseScope;
}) {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [query, setQuery] = useState("");
  const [queryDraft, setQueryDraft] = useState("");
  const [selected, setSelected] = useState<ReleaseArtifactRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const { mutate: mutateGlobal } = useSWRConfig();
  const artifacts = useSWR(
    [
      "release-artifacts",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
      query,
      includeArchived,
    ],
    () =>
      listReleaseArtifacts(scope, {
        includeArchived,
        pageSize: 100,
        pageToken: "",
        query,
      }),
    { keepPreviousData: true },
  );

  async function inspect(artifact: ReleaseArtifactRecord) {
    setGettingId(artifact.id);
    try {
      setSelected(await getReleaseArtifact(scope, artifact.id));
      toast.success(translate("Artifact details refreshed."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setGettingId("");
    }
  }

  async function changed(artifact: ReleaseArtifactRecord) {
    setSelected(artifact);
    await artifacts.mutate();
    await mutateGlobal([
      "release-upload-artifacts",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
    ]);
    await mutateGlobal(
      [
        "release-form-artifacts",
        scope.tenantId,
        scope.applicationId,
        scope.environmentId,
      ],
      undefined,
      { revalidate: false },
    );
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(26rem,0.85fr)]">
      <div className="space-y-6">
        <Card data-ui-action="list-release-artifacts">
          <CardHeader>
            <CardTitle>{translate("Verified artifact inventory")}</CardTitle>
            <CardDescription>
              {translate("Each runtime mapping is content-addressed and signed before it can enter a release manifest.")}</CardDescription>
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
                <span className="sr-only">{translate("Search release artifacts")}</span>
                <Search
                  aria-hidden="true"
                  className="absolute left-3 top-3 size-4 text-slate-600"
                />
                <input
                  className={cn(releaseInputClassName, "pl-9")}
                  name="artifactSearch"
                  onChange={(event) => setQueryDraft(event.target.value)}
                  placeholder={translate("Search version, runtime, or file name")}
                  value={queryDraft}
                />
              </label>
              <Button type="submit" variant="outline">
                {translate("Apply")}</Button>
            </form>
            <label className="flex items-center gap-2 text-xs text-slate-400">
              <input
                aria-label={translate("Include archived release artifacts")}
                checked={includeArchived}
                onChange={(event) => setIncludeArchived(event.target.checked)}
                type="checkbox"
              />
              {translate("Include archived artifacts")}</label>
            {artifacts.isLoading ? (
              <ReleaseLoadingState label={translate("Loading artifacts")} />
            ) : artifacts.error ? (
              <ReleaseErrorState error={artifacts.error} />
            ) : (artifacts.data?.artifacts.length ?? 0) === 0 ? (
              <ReleaseEmptyState message={translate("No release artifacts match this view.")} />
            ) : (
              <div className="space-y-2">
                {artifacts.data?.artifacts.map((artifact) => (
                  <article
                    className={cn(
                      "rounded-xl border p-4 transition",
                      selected?.id === artifact.id
                        ? "border-violet-400/30 bg-violet-400/[0.06]"
                        : "border-white/8 bg-white/[0.02] hover:border-white/15",
                    )}
                    data-testid={`release-artifact-${artifact.releaseVersion}-${artifact.targetRuntimeId}-${artifact.artifactKind}`}
                    key={artifact.id}
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <button
                        className="min-w-0 text-left"
                        onClick={() => setSelected(artifact)}
                        type="button"
                      >
                        <span className="flex flex-wrap items-center gap-2">
                          <FileKey2 aria-hidden="true" className="size-3.5 text-violet-300" />
                          <span className="text-sm font-medium text-white">
                            {artifact.fileName}
                          </span>
                          <ReleaseStatusBadge status={artifact.status} />
                        </span>
                        <span className="mt-1 block font-mono text-xs text-violet-300">
                          {artifact.releaseVersion} · {artifact.targetRuntimeId} ·{" "}
                          {translate(artifact.artifactKind.endsWith("_DELTA") ? "delta" : "full")}
                        </span>
                        <span className="mt-2 block truncate font-mono text-[11px] text-slate-600">
                          {translate("sha256")} {artifact.sha256}
                        </span>
                      </button>
                      <Button
                        data-ui-action="get-release-artifact"
                        disabled={gettingId === artifact.id}
                        onClick={() => void inspect(artifact)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {gettingId === artifact.id ? (
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
        <ArtifactUploadExperience
          csrfToken={csrfToken}
          onCompleted={changed}
          scope={scope}
        />
      </div>

      {selected ? (
        <ArtifactInspector
          artifact={selected}
          csrfToken={csrfToken}
          key={`${selected.id}:${selected.version}`}
          onChanged={changed}
        />
      ) : (
        <ReleaseEmptyState message={translate("Select an artifact to inspect verification and storage metadata.")} />
      )}
    </div>
  );
}

function ArtifactUploadExperience({
  csrfToken,
  onCompleted,
  scope,
}: {
  csrfToken: string;
  onCompleted: (artifact: ReleaseArtifactRecord) => Promise<void>;
  scope: ReleaseScope;
}) {
  const [mode, setMode] = useState<"quick" | "advanced">("quick");
  return (
    <div className="space-y-3">
      <div
        aria-label={translate("Artifact upload method")}
        className="grid gap-2 rounded-xl border border-white/8 bg-white/[0.025] p-2 sm:grid-cols-2"
        role="tablist"
      >
        <button
          aria-selected={mode === "quick"}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2.5 text-left transition",
            mode === "quick"
              ? "bg-violet-400/15 text-violet-100"
              : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
          )}
          data-ui-action="select-velopack-quick-upload"
          onClick={() => setMode("quick")}
          role="tab"
          type="button"
        >
          <Sparkles aria-hidden="true" className="size-4 shrink-0" />
          <span>
            <span className="block text-xs font-medium">
              {translate("C# Velopack quick upload")}
            </span>
            <span className="mt-0.5 block text-[10px] opacity-70">
              {translate("Recommended · automatic package inspection")}
            </span>
          </span>
        </button>
        <button
          aria-selected={mode === "advanced"}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2.5 text-left transition",
            mode === "advanced"
              ? "bg-sky-400/15 text-sky-100"
              : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200",
          )}
          data-ui-action="select-advanced-artifact-upload"
          onClick={() => setMode("advanced")}
          role="tab"
          type="button"
        >
          <SlidersHorizontal aria-hidden="true" className="size-4 shrink-0" />
          <span>
            <span className="block text-xs font-medium">
              {translate("Advanced upload")}
            </span>
            <span className="mt-0.5 block text-[10px] opacity-70">
              {translate("Manual metadata and non-Velopack artifacts")}
            </span>
          </span>
        </button>
      </div>
      {mode === "quick" ? (
        <VelopackQuickUploadCard
          csrfToken={csrfToken}
          onCompleted={onCompleted}
          scope={scope}
        />
      ) : (
        <ArtifactUploadCard
          csrfToken={csrfToken}
          onCompleted={onCompleted}
          scope={scope}
        />
      )}
    </div>
  );
}

function ArtifactUploadCard({
  csrfToken,
  onCompleted,
  scope,
}: {
  csrfToken: string;
  onCompleted: (artifact: ReleaseArtifactRecord) => Promise<void>;
  scope: ReleaseScope;
}) {
  const keys = useSWR(["release-upload-keys", scope.tenantId], () =>
    listReleaseSigningKeys(scope.tenantId, {
      includeArchived: false,
      pageSize: 100,
      pageToken: "",
      query: "",
    }),
  );
  const artifacts = useSWR(
    [
      "release-upload-artifacts",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
    ],
    () =>
      listReleaseArtifacts(scope, {
        includeArchived: false,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const [file, setFile] = useState<File | null>(null);
  const [sha256, setSha256] = useState("");
  const [releaseVersion, setReleaseVersion] = useState("");
  const [targetRuntimeId, setTargetRuntimeId] = useState("win-x64");
  const [artifactKind, setArtifactKind] = useState<ReleaseArtifactKind>(
    ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL,
  );
  const [deltaFromVersion, setDeltaFromVersion] = useState("");
  const [contentType, setContentType] = useState("application/octet-stream");
  const [signingKeyId, setSigningKeyId] = useState("");
  const [signature, setSignature] = useState("");
  const [pending, setPending] = useState<{
    file: File;
    upload: ReleaseArtifactUploadRecord;
  } | null>(null);
  const [busy, setBusy] = useState("");

  async function chooseFile(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.files?.[0] ?? null;
    setFile(next);
    setPending(null);
    setSignature("");
    if (!next) {
      setSha256("");
      return;
    }
    setBusy("hash");
    try {
      setSha256(await sha256Hex(next));
      setContentType(next.type || "application/octet-stream");
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
      setSha256("");
    } finally {
      setBusy("");
    }
  }

  async function begin(event: FormEvent) {
    event.preventDefault();
    if (!file || !sha256) {
      toast.error(translate("Choose a file and wait for SHA-256 calculation."));
      return;
    }
    setBusy("begin");
    try {
      const upload = await createReleaseArtifactUpload(csrfToken, scope, {
        artifactKind,
        contentType,
        deltaFromVersion,
        fileName: file.name,
        releaseVersion,
        sha256,
        signature,
        signingKeyId,
        sizeBytes: file.size,
        targetRuntimeId,
      });
      setPending({ file, upload });
      toast.success(translate("Artifact upload ticket created."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  async function complete() {
    if (!pending) return;
    setBusy("complete");
    try {
      await uploadReleaseArtifactTransfer(csrfToken, pending.upload, pending.file);
      const artifact = await completeReleaseArtifactUpload(
        csrfToken,
        pending.upload.artifact,
      );
      await onCompleted(artifact);
      setPending(null);
      setFile(null);
      setSha256("");
      setSignature("");
      toast.success(
        translate(artifact.status === ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED
          ? "Artifact uploaded and signature verified."
          : "Artifact upload completed, but signature verification was rejected."),
      );
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{translate("Upload signed artifact")}</CardTitle>
        <CardDescription>
          {translate("Sign the lowercase SHA-256 text with RSA-PSS/SHA-256 (32-byte salt), then provide the Base64 signature before requesting a short-lived upload ticket.")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={begin}>
          <label className={cn(releaseLabelClassName, "sm:col-span-2")}>
            {translate("Artifact file")}<input
              className={cn(releaseInputClassName, "py-2")}
              disabled={Boolean(pending)}
              name="releaseArtifactFile"
              onChange={(event) => void chooseFile(event)}
              required
              type="file"
            />
          </label>
          <label className={releaseLabelClassName}>
            {translate("Release version")}<input
              className={releaseInputClassName}
              disabled={Boolean(pending)}
              name="artifactReleaseVersion"
              onChange={(event) => setReleaseVersion(event.target.value)}
              placeholder="1.0.0"
              required
              value={releaseVersion}
            />
          </label>
          <label className={releaseLabelClassName}>
            {translate("Target runtime")}<input
              className={releaseInputClassName}
              disabled={Boolean(pending)}
              name="artifactRuntimeId"
              onChange={(event) => {
                setTargetRuntimeId(event.target.value);
                setDeltaFromVersion("");
              }}
              required
              value={targetRuntimeId}
            />
          </label>
          <label className={releaseLabelClassName}>
            {translate("Artifact kind")}<select
              className={releaseInputClassName}
              disabled={Boolean(pending)}
              name="artifactKind"
              onChange={(event) => {
                setArtifactKind(event.target.value as ReleaseArtifactKind);
                if (event.target.value.endsWith("_FULL")) setDeltaFromVersion("");
              }}
              value={artifactKind}
            >
              <option value={ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL}>
                {translate("Full package")}</option>
              <option value={ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA}>
                {translate("Delta package")}</option>
            </select>
          </label>
          <SearchableSelect
            ariaLabel={translate("Delta from version")}
            className={releaseInputClassName}
            disabled={
              Boolean(pending) ||
              artifactKind === ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL
            }
            emptyLabel={translate("Select a verified full artifact version")}
            label={translate("Delta from version")}
            labelClassName={releaseLabelClassName}
            name="artifactDeltaFromVersion"
            onChange={setDeltaFromVersion}
            options={(artifacts.data?.artifacts ?? [])
              .filter(
                (artifact) =>
                  artifact.status ===
                    ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED &&
                  artifact.artifactKind ===
                    ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL &&
                  artifact.targetRuntimeId === targetRuntimeId,
              )
              .map((artifact) => ({
                label: `${artifact.releaseVersion} (${artifact.fileName})`,
                value: artifact.releaseVersion,
              }))}
            required={
              artifactKind === ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA
            }
            value={deltaFromVersion}
          />
          <label className={releaseLabelClassName}>
            {translate("Content type")}<input
              className={releaseInputClassName}
              disabled={Boolean(pending)}
              name="artifactContentType"
              onChange={(event) => setContentType(event.target.value)}
              required
              value={contentType}
            />
          </label>
          <label className={releaseLabelClassName}>
            {translate("Signing key")}<select
              className={releaseInputClassName}
              disabled={Boolean(pending)}
              name="artifactSigningKey"
              onChange={(event) => setSigningKeyId(event.target.value)}
              required
              value={signingKeyId}
            >
              <option value="">{translate("Choose a key")}</option>
              {(keys.data?.signingKeys ?? []).map((key) => (
                <option key={key.id} value={key.id}>
                  {key.displayName} ({key.key})
                </option>
              ))}
            </select>
          </label>
          <div className="sm:col-span-2 rounded-xl border border-white/8 bg-slate-950/60 p-4">
            <p className="text-xs font-medium text-slate-400">{translate("SHA-256 signing input")}</p>
            <output
              className="mt-2 block break-all font-mono text-xs text-violet-300"
              data-testid="artifact-sha256"
            >
              {busy === "hash" ? "Calculating…" : sha256 || "Choose a file"}
            </output>
          </div>
          <label className={cn(releaseLabelClassName, "sm:col-span-2")}>
            {translate("Detached signature (Base64)")}<textarea
              className={cn(releaseTextAreaClassName, "h-28 font-mono text-xs")}
              disabled={Boolean(pending)}
              name="artifactSignature"
              onChange={(event) => setSignature(event.target.value)}
              required
              value={signature}
            />
          </label>
          <div className="sm:col-span-2">
            <Button
              data-ui-action="create-release-artifact-upload"
              disabled={Boolean(busy) || Boolean(pending) || !sha256}
              type="submit"
            >
              {busy === "begin" ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <PackagePlus aria-hidden="true" className="size-4" />
              )}
              {translate("Create upload ticket")}</Button>
          </div>
        </form>

        {pending && (
          <div className="rounded-xl border border-amber-400/20 bg-amber-400/[0.06] p-4">
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className="text-sm font-medium text-amber-200">{translate("Upload ticket ready")}</p>
                <p className="mt-1 font-mono text-xs text-slate-500">
                  {pending.upload.uploadSession.id}
                </p>
              </div>
              <Button
                data-ui-action="complete-release-artifact-upload"
                disabled={Boolean(busy)}
                onClick={() => void complete()}
                type="button"
              >
                {busy === "complete" ? (
                  <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                ) : (
                  <Upload aria-hidden="true" className="size-4" />
                )}
                {translate("Upload and verify")}</Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ArtifactInspector({
  artifact,
  csrfToken,
  onChanged,
}: {
  artifact: ReleaseArtifactRecord;
  csrfToken: string;
  onChanged: (artifact: ReleaseArtifactRecord) => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const archived =
    artifact.status === ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_ARCHIVED;
  const uploading =
    artifact.status === ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_UPLOADING;

  async function archive() {
    setBusy(true);
    try {
      const updated = await archiveReleaseArtifact(csrfToken, artifact);
      await onChanged(updated);
      toast.success(translate("Release artifact archived."));
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="truncate">{artifact.fileName}</CardTitle>
          <ReleaseStatusBadge status={artifact.status} />
        </div>
        <CardDescription>
          {artifact.releaseVersion} · {artifact.targetRuntimeId}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <dl className="space-y-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-xs">
          <Detail label={translate("Artifact ID")} value={artifact.id} />
          <Detail label={translate("Kind")} value={artifact.artifactKind} />
          <Detail label={translate("Delta source")} value={artifact.deltaFromVersion || translate("None")} />
          <Detail label={translate("Content type")} value={artifact.contentType} />
          <Detail label={translate("Size")} value={`${formatNumber(artifact.sizeBytes)} ${translate("bytes")}`} />
          <Detail label={translate("SHA-256")} value={artifact.sha256} mono />
          <Detail label={translate("Signing key ID")} value={artifact.signingKeyId} mono />
          <Detail label={translate("Storage object ID")} value={artifact.storageObjectId} mono />
        </dl>
        {artifact.failureReason && (
          <div className="rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4 text-xs text-rose-200">
            {artifact.failureReason}
          </div>
        )}
        {!archived && (
          <Button
            data-ui-action="archive-release-artifact"
            disabled={busy || uploading}
            onClick={() => void archive()}
            title={uploading ? "Complete the upload before archiving." : undefined}
            type="button"
            variant="outline"
          >
            {busy ? (
              <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
            ) : (
              <Archive aria-hidden="true" className="size-4" />
            )}
            {translate("Archive artifact")}</Button>
        )}
        {artifact.status ===
          ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED && (
          <p className="flex items-center gap-2 text-xs text-emerald-300">
            <ShieldCheck aria-hidden="true" className="size-4" />
            {translate("Storage hash and RSA-PSS signature verified.")}</p>
        )}
        {archived && (
          <p className="flex items-center gap-2 text-xs text-slate-500">
            <CheckCircle2 aria-hidden="true" className="size-4" />
            {translate("Archived artifacts remain immutable for audit history.")}</p>
        )}
      </CardContent>
    </Card>
  );
}

function Detail({
  label,
  mono = false,
  value,
}: {
  label: string;
  mono?: boolean;
  value: string;
}) {
  return (
    <div>
      <dt className="text-slate-600">{label}</dt>
      <dd className={cn("mt-1 break-all text-slate-300", mono && "font-mono")}>
        {value}
      </dd>
    </div>
  );
}
