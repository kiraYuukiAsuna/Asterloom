"use client";

import {
  AlertTriangle,
  CheckCircle2,
  FileArchive,
  FileJson2,
  LoaderCircle,
  RotateCcw,
  ShieldCheck,
  UploadCloud,
} from "lucide-react";
import { type ChangeEvent, useMemo, useState } from "react";
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
  ReleaseArtifactKindObject,
  ReleaseArtifactStatusObject,
} from "@/lib/api/generated/models";
import {
  completeReleaseArtifactUpload,
  createReleaseArtifactUpload,
  listReleaseArtifacts,
  listReleaseSigningKeys,
  releaseErrorMessage,
  uploadReleaseArtifactTransfer,
  type ReleaseArtifactRecord,
  type ReleaseScope,
  type ReleaseSigningKeyRecord,
} from "@/lib/api/release-management";
import { sha256Hex } from "@/lib/api/storage-management";
import { translate } from "@/lib/i18n/locale";
import {
  compareSemanticVersions,
  inspectVelopackPackage,
  readVelopackSigningBundle,
  type VelopackPackageMetadata,
  type VelopackSigningArtifact,
  type VelopackSigningBundle,
} from "@/lib/release/velopack-package";
import { cn } from "@/lib/utils/cn";

import { releaseInputClassName, releaseLabelClassName } from "./release-ui";

type PackageOperation = "idle" | "uploading" | "verified" | "failed";

type AnalyzedPackage = {
  analysisError: string;
  file: File;
  id: string;
  metadata: VelopackPackageMetadata | null;
  operation: PackageOperation;
  operationError: string;
  sha256: string;
};

type PreparedPackage = AnalyzedPackage & {
  blockers: string[];
  deltaFromVersion: string;
  existingArtifact: ReleaseArtifactRecord | null;
  signingArtifact: VelopackSigningArtifact | null;
  signingKey: ReleaseSigningKeyRecord | null;
  sourceVersions: string[];
};

export function VelopackQuickUploadCard({
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
  const inventory = useSWR(
    [
      "release-quick-upload-artifacts",
      scope.tenantId,
      scope.applicationId,
      scope.environmentId,
    ],
    () =>
      listReleaseArtifacts(scope, {
        includeArchived: true,
        pageSize: 100,
        pageToken: "",
        query: "",
      }),
  );
  const [packages, setPackages] = useState<AnalyzedPackage[]>([]);
  const [signingBundle, setSigningBundle] = useState<VelopackSigningBundle | null>(
    null,
  );
  const [signingBundleName, setSigningBundleName] = useState("");
  const [signingBundleError, setSigningBundleError] = useState("");
  const [deltaOverrides, setDeltaOverrides] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<"" | "analyzing" | "uploading">("");
  const [inputRevision, setInputRevision] = useState(0);

  const prepared = useMemo(
    () =>
      preparePackages(
        packages,
        signingBundle,
        keys.data?.signingKeys ?? [],
        inventory.data?.artifacts ?? [],
        deltaOverrides,
      ),
    [
      deltaOverrides,
      inventory.data?.artifacts,
      keys.data?.signingKeys,
      packages,
      signingBundle,
    ],
  );
  const uploadable = prepared.filter(
    (item) =>
      item.blockers.length === 0 &&
      !item.existingArtifact &&
      item.operation !== "verified",
  );
  const ready =
    busy === "" &&
    Boolean(signingBundle) &&
    packages.length > 0 &&
    uploadable.length > 0 &&
    prepared.every(
      (item) => item.blockers.length === 0 || Boolean(item.existingArtifact),
    );

  async function choosePackages(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    setPackages(
      files.map((file, index) => ({
        analysisError: "",
        file,
        id: `${file.name}:${file.size}:${file.lastModified}:${index}`,
        metadata: null,
        operation: "idle",
        operationError: "",
        sha256: "",
      })),
    );
    setDeltaOverrides({});
    if (files.length === 0) return;

    setBusy("analyzing");
    for (const [index, file] of files.entries()) {
      const id = `${file.name}:${file.size}:${file.lastModified}:${index}`;
      try {
        const [metadata, sha256] = await Promise.all([
          inspectVelopackPackage(file),
          sha256Hex(file),
        ]);
        setPackages((current) =>
          current.map((item) =>
            item.id === id ? { ...item, metadata, sha256 } : item,
          ),
        );
      } catch (error) {
        setPackages((current) =>
          current.map((item) =>
            item.id === id
              ? { ...item, analysisError: releaseErrorMessage(error) }
              : item,
          ),
        );
      }
    }
    setBusy("");
  }

  async function chooseSigningBundle(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    setSigningBundle(null);
    setSigningBundleError("");
    setSigningBundleName(file?.name ?? "");
    if (!file) return;
    try {
      setSigningBundle(await readVelopackSigningBundle(file));
    } catch (error) {
      setSigningBundleError(releaseErrorMessage(error));
    }
  }

  function reset() {
    setPackages([]);
    setSigningBundle(null);
    setSigningBundleName("");
    setSigningBundleError("");
    setDeltaOverrides({});
    setInputRevision((value) => value + 1);
  }

  async function uploadAll() {
    if (!ready) return;
    setBusy("uploading");
    let failed = 0;
    let verified = 0;
    let lastCompleted: ReleaseArtifactRecord | null = null;
    const verifiedFullVersions = new Set(
      (inventory.data?.artifacts ?? [])
        .filter(
          (artifact) =>
            artifact.status ===
              ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED &&
            artifact.artifactKind ===
              ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL,
        )
        .map((artifact) => fullVersionKey(artifact.targetRuntimeId, artifact.releaseVersion)),
    );
    const ordered = [...uploadable].sort(compareUploadOrder);

    for (const item of ordered) {
      setPackageOperation(item.id, "uploading", "");
      try {
        if (
          item.metadata?.artifactKind ===
            ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA &&
          !verifiedFullVersions.has(
            fullVersionKey(item.metadata.targetRuntimeId, item.deltaFromVersion),
          )
        ) {
          throw new Error(
            "The selected delta source full package was not verified. Upload it first or choose another source.",
          );
        }
        if (!item.metadata || !item.signingArtifact || !item.signingKey) {
          throw new Error("The Velopack package preparation is incomplete.");
        }

        const upload = await createReleaseArtifactUpload(csrfToken, scope, {
          artifactKind: item.metadata.artifactKind,
          contentType: "application/octet-stream",
          deltaFromVersion: item.deltaFromVersion,
          fileName: item.file.name,
          releaseVersion: item.metadata.releaseVersion,
          sha256: item.sha256,
          signature: item.signingArtifact.signature,
          signingKeyId: item.signingKey.id,
          sizeBytes: item.file.size,
          targetRuntimeId: item.metadata.targetRuntimeId,
          validateVelopackPackage: true,
        });
        await uploadReleaseArtifactTransfer(csrfToken, upload, item.file);
        const completed = await completeReleaseArtifactUpload(
          csrfToken,
          upload.artifact,
        );
        lastCompleted = completed;
        if (
          completed.status !==
          ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED
        ) {
          throw new Error(
            completed.failureReason ||
              "The server rejected the Velopack package during verification.",
          );
        }
        if (
          item.metadata.artifactKind ===
          ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL
        ) {
          verifiedFullVersions.add(
            fullVersionKey(
              item.metadata.targetRuntimeId,
              item.metadata.releaseVersion,
            ),
          );
        }
        setPackageOperation(item.id, "verified", "");
        verified += 1;
      } catch (error) {
        setPackageOperation(item.id, "failed", releaseErrorMessage(error));
        failed += 1;
      }
    }

    try {
      if (lastCompleted) await onCompleted(lastCompleted);
      await inventory.mutate();
    } catch (error) {
      toast.error(translate(releaseErrorMessage(error)));
    } finally {
      setBusy("");
    }
    if (failed === 0) {
      toast.success(
        translate("All Velopack packages were uploaded and verified."),
      );
    } else {
      toast.error(
        translate("{0} package(s) verified; {1} package(s) failed.", {
          0: verified,
          1: failed,
        }),
      );
    }
  }

  function setPackageOperation(
    id: string,
    operation: PackageOperation,
    operationError: string,
  ) {
    setPackages((current) =>
      current.map((item) =>
        item.id === id ? { ...item, operation, operationError } : item,
      ),
    );
  }

  return (
    <Card data-ui-action="quick-upload-velopack-artifacts">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{translate("C# Velopack quick upload")}</CardTitle>
            <CardDescription>
              {translate(
                "Choose Velopack packages and one signing bundle. Version, channel, runtime, package kind, hash, signature, and signing key are matched automatically.",
              )}
            </CardDescription>
          </div>
          <span className="inline-flex w-fit items-center gap-1.5 rounded-full border border-emerald-400/20 bg-emerald-400/10 px-2.5 py-1 text-[11px] font-medium text-emerald-300">
            <ShieldCheck aria-hidden="true" className="size-3.5" />
            {translate("Server-inspected NuSpec")}
          </span>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="grid gap-3 md:grid-cols-2">
          <label className="group rounded-xl border border-dashed border-violet-400/25 bg-violet-400/[0.04] p-4 transition hover:border-violet-400/45">
            <span className="flex items-center gap-2 text-sm font-medium text-white">
              <FileArchive aria-hidden="true" className="size-4 text-violet-300" />
              {translate("Velopack packages")}
            </span>
            <span className="mt-1 block text-xs leading-5 text-slate-500">
              {translate("Select one or more *-full.nupkg / *-delta.nupkg files.")}
            </span>
            <input
              accept=".nupkg,application/octet-stream"
              className={cn(releaseInputClassName, "mt-3 py-2")}
              disabled={Boolean(busy)}
              key={`packages:${inputRevision}`}
              multiple
              name="velopackPackages"
              onChange={(event) => void choosePackages(event)}
              type="file"
            />
          </label>
          <label className="group rounded-xl border border-dashed border-sky-400/25 bg-sky-400/[0.04] p-4 transition hover:border-sky-400/45">
            <span className="flex items-center gap-2 text-sm font-medium text-white">
              <FileJson2 aria-hidden="true" className="size-4 text-sky-300" />
              {translate("Signing bundle")}
            </span>
            <span className="mt-1 block text-xs leading-5 text-slate-500">
              {translate(
                "Select signing-metadata.json. It contains signatures and a public-key fingerprint, never the private key.",
              )}
            </span>
            <input
              accept="application/json,.json"
              className={cn(releaseInputClassName, "mt-3 py-2")}
              disabled={busy === "uploading"}
              key={`bundle:${inputRevision}`}
              name="velopackSigningBundle"
              onChange={(event) => void chooseSigningBundle(event)}
              type="file"
            />
          </label>
        </div>

        {signingBundleError ? (
          <InlineProblem message={signingBundleError} />
        ) : signingBundle ? (
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-xl border border-emerald-400/20 bg-emerald-400/[0.05] px-4 py-3 text-xs">
            <span className="font-medium text-emerald-300">
              {signingBundleName}
            </span>
            <span className="font-mono text-slate-500">
              {translate("fingerprint")} {signingBundle.fingerprint}
            </span>
          </div>
        ) : null}

        {packages.length === 0 ? (
          <div className="rounded-xl border border-white/8 bg-white/[0.02] p-6 text-center text-sm text-slate-500">
            {translate("Select the generated Velopack packages to begin automatic inspection.")}
          </div>
        ) : (
          <div className="space-y-2" data-testid="velopack-package-list">
            {prepared.map((item) => (
              <PackageRow
                item={item}
                key={item.id}
                onDeltaSourceChange={(version) =>
                  setDeltaOverrides((current) => ({
                    ...current,
                    [item.id]: version,
                  }))
                }
              />
            ))}
          </div>
        )}

        <div className="flex flex-col gap-3 border-t border-white/8 pt-5 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-xs leading-5 text-slate-500">
            {translate(
              "The browser only prepares metadata. The server still verifies size, SHA-256, RSA-PSS signature, file name, NuSpec version, RID, and Full/Delta kind.",
            )}
          </p>
          <div className="flex shrink-0 gap-2">
            <Button
              disabled={Boolean(busy) || packages.length === 0}
              onClick={reset}
              type="button"
              variant="outline"
            >
              <RotateCcw aria-hidden="true" className="size-4" />
              {translate("Reset")}
            </Button>
            <Button
              data-ui-action="upload-velopack-packages"
              disabled={!ready}
              onClick={() => void uploadAll()}
              type="button"
            >
              {busy ? (
                <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
              ) : (
                <UploadCloud aria-hidden="true" className="size-4" />
              )}
              {busy === "analyzing"
                ? translate("Inspecting packages")
                : busy === "uploading"
                  ? translate("Uploading packages")
                  : translate("Upload and verify all")}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function PackageRow({
  item,
  onDeltaSourceChange,
}: {
  item: PreparedPackage;
  onDeltaSourceChange: (version: string) => void;
}) {
  const metadata = item.metadata;
  const successful =
    item.operation === "verified" || Boolean(item.existingArtifact);
  const problem = item.analysisError || item.operationError || item.blockers[0] || "";
  return (
    <article
      className={cn(
        "rounded-xl border p-4",
        successful
          ? "border-emerald-400/20 bg-emerald-400/[0.04]"
          : problem
            ? "border-rose-400/20 bg-rose-400/[0.04]"
            : "border-white/8 bg-white/[0.02]",
      )}
      data-testid={`velopack-package-${item.file.name}`}
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <p className="flex items-center gap-2 text-sm font-medium text-white">
            <FileArchive aria-hidden="true" className="size-4 shrink-0 text-violet-300" />
            <span className="truncate">{item.file.name}</span>
          </p>
          {metadata ? (
            <p className="mt-1 font-mono text-[11px] text-slate-500">
              {metadata.packageId} · {metadata.releaseVersion} · {metadata.channel} ·{" "}
              {metadata.targetRuntimeId} ·{" "}
              {translate(
                metadata.artifactKind ===
                  ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL
                  ? "full"
                  : "delta",
              )}
            </p>
          ) : (
            <p className="mt-1 text-xs text-slate-500">
              {item.analysisError
                ? translate(item.analysisError)
                : translate("Waiting for package inspection")}
            </p>
          )}
          {item.sha256 && (
            <p className="mt-2 truncate font-mono text-[10px] text-slate-600">
              {item.sha256}
            </p>
          )}
        </div>
        <PackageState item={item} />
      </div>

      {metadata?.artifactKind ===
        ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA &&
        item.sourceVersions.length > 0 && (
          <label className={cn(releaseLabelClassName, "mt-3 max-w-xs")}>
            {translate("Delta source full version")}
            <select
              className={releaseInputClassName}
              disabled={item.operation === "uploading" || item.operation === "verified"}
              name={`deltaSource:${item.id}`}
              onChange={(event) => onDeltaSourceChange(event.target.value)}
              value={item.deltaFromVersion}
            >
              {item.sourceVersions.map((version) => (
                <option key={version} value={version}>
                  {version}
                </option>
              ))}
            </select>
          </label>
        )}

      {problem && (
        <p className="mt-3 flex items-start gap-2 text-xs leading-5 text-rose-300">
          <AlertTriangle aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" />
          {translate(problem)}
        </p>
      )}
    </article>
  );
}

function PackageState({ item }: { item: PreparedPackage }) {
  if (item.operation === "uploading") {
    return <StateLabel icon={LoaderCircle} label="Uploading" spin />;
  }
  if (item.operation === "verified") {
    return <StateLabel icon={CheckCircle2} label="Verified" tone="success" />;
  }
  if (item.operation === "failed") {
    return <StateLabel icon={AlertTriangle} label="Failed" tone="danger" />;
  }
  if (item.existingArtifact) {
    return <StateLabel icon={CheckCircle2} label="Already verified" tone="success" />;
  }
  if (item.blockers.length === 0 && item.metadata) {
    return <StateLabel icon={ShieldCheck} label="Ready" tone="success" />;
  }
  return <StateLabel icon={LoaderCircle} label="Needs attention" />;
}

function StateLabel({
  icon: Icon,
  label,
  spin = false,
  tone = "muted",
}: {
  icon: typeof LoaderCircle;
  label: string;
  spin?: boolean;
  tone?: "danger" | "muted" | "success";
}) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-medium",
        tone === "success"
          ? "border-emerald-400/20 bg-emerald-400/10 text-emerald-300"
          : tone === "danger"
            ? "border-rose-400/20 bg-rose-400/10 text-rose-300"
            : "border-white/10 bg-white/[0.03] text-slate-400",
      )}
    >
      <Icon aria-hidden="true" className={cn("size-3.5", spin && "animate-spin")} />
      {translate(label)}
    </span>
  );
}

function InlineProblem({ message }: { message: string }) {
  return (
    <p className="flex items-start gap-2 rounded-xl border border-rose-400/20 bg-rose-400/[0.05] px-4 py-3 text-xs leading-5 text-rose-300">
      <AlertTriangle aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" />
      {translate(message)}
    </p>
  );
}

function preparePackages(
  packages: AnalyzedPackage[],
  signingBundle: VelopackSigningBundle | null,
  signingKeys: ReleaseSigningKeyRecord[],
  inventory: ReleaseArtifactRecord[],
  deltaOverrides: Record<string, string>,
): PreparedPackage[] {
  const firstMetadata = packages.find((item) => item.metadata)?.metadata ?? null;
  const duplicateNames = new Set(
    packages
      .map((item) => item.file.name)
      .filter((name, index, names) => names.indexOf(name) !== index),
  );
  const duplicateIdentities = new Set(
    packages
      .flatMap((item) =>
        item.metadata ? [packageIdentityKey(item.metadata)] : [],
      )
      .filter((identity, index, identities) =>
        identities.indexOf(identity) !== index,
      ),
  );

  return packages.map((item) => {
    const blockers: string[] = [];
    const metadata = item.metadata;
    if (item.analysisError) blockers.push(item.analysisError);
    if (!metadata && !item.analysisError) blockers.push("Waiting for package inspection");
    if (duplicateNames.has(item.file.name)) {
      blockers.push("Package file names must be unique within one batch.");
    }
    if (metadata && duplicateIdentities.has(packageIdentityKey(metadata))) {
      blockers.push(
        "A quick-upload batch cannot contain duplicate version, runtime, and package-kind identities.",
      );
    }
    if (
      metadata &&
      firstMetadata &&
      (metadata.packageId.toLowerCase() !== firstMetadata.packageId.toLowerCase() ||
        metadata.channel !== firstMetadata.channel)
    ) {
      blockers.push(
        "One quick-upload batch must use the same NuSpec package ID and channel.",
      );
    }

    const signingArtifact = signingBundle?.artifacts[item.file.name] ?? null;
    const signingKey = signingBundle
      ? (signingKeys.find(
          (key) => key.fingerprint === signingBundle.fingerprint,
        ) ?? null)
      : null;
    if (!signingBundle) {
      blockers.push("Choose the signing-metadata.json bundle.");
    } else {
      if (!signingArtifact) {
        blockers.push("The signing bundle has no entry for this package file name.");
      } else if (item.sha256 && signingArtifact.sha256 !== item.sha256) {
        blockers.push("The package SHA-256 does not match the signing bundle.");
      }
      if (!signingKey) {
        blockers.push(
          "No active server signing key matches the bundle fingerprint. Register its public key first.",
        );
      }
    }

    const sourceVersions = metadata
      ? collectDeltaSourceVersions(metadata, packages, inventory)
      : [];
    const selectedOverride = deltaOverrides[item.id];
    const deltaFromVersion =
      selectedOverride && sourceVersions.includes(selectedOverride)
        ? selectedOverride
        : (sourceVersions[0] ?? "");
    const identityArtifact = metadata
      ? (inventory.find(
          (artifact) =>
            artifact.releaseVersion === metadata.releaseVersion &&
            artifact.targetRuntimeId === metadata.targetRuntimeId &&
            artifact.artifactKind === metadata.artifactKind &&
            artifact.deltaFromVersion === deltaFromVersion,
        ) ?? null)
      : null;
    const existingArtifact =
      identityArtifact?.status ===
        ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED &&
      identityArtifact.sha256 === item.sha256
        ? identityArtifact
        : null;
    if (identityArtifact && !existingArtifact) {
      blockers.push(
        "An artifact already uses this version, runtime, kind, and delta source with different content or status. Use a new release version.",
      );
    }
    if (
      metadata?.artifactKind ===
        ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA &&
      !deltaFromVersion
    ) {
      blockers.push(
        "No older verified or selected full package exists for this runtime. Add it to this batch or use advanced upload.",
      );
    }

    return {
      ...item,
      blockers: existingArtifact ? [] : unique(blockers),
      deltaFromVersion,
      existingArtifact,
      signingArtifact,
      signingKey,
      sourceVersions,
    };
  });
}

function collectDeltaSourceVersions(
  metadata: VelopackPackageMetadata,
  packages: AnalyzedPackage[],
  inventory: ReleaseArtifactRecord[],
) {
  if (
    metadata.artifactKind !==
    ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA
  ) {
    return [];
  }
  const versions = [
    ...packages
      .filter(
        (item) =>
          item.metadata?.artifactKind ===
            ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL &&
          item.metadata.targetRuntimeId === metadata.targetRuntimeId &&
          compareSemanticVersions(
            item.metadata.releaseVersion,
            metadata.releaseVersion,
          ) < 0,
      )
      .map((item) => item.metadata?.releaseVersion ?? ""),
    ...inventory
      .filter(
        (artifact) =>
          artifact.status ===
            ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED &&
          artifact.artifactKind ===
            ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL &&
          artifact.targetRuntimeId === metadata.targetRuntimeId &&
          compareSemanticVersions(artifact.releaseVersion, metadata.releaseVersion) < 0,
      )
      .map((artifact) => artifact.releaseVersion),
  ];
  return unique(versions).sort((left, right) =>
    compareSemanticVersions(right, left),
  );
}

function compareUploadOrder(left: PreparedPackage, right: PreparedPackage) {
  const leftDelta =
    left.metadata?.artifactKind ===
    ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA;
  const rightDelta =
    right.metadata?.artifactKind ===
    ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA;
  if (leftDelta !== rightDelta) return leftDelta ? 1 : -1;
  if (!left.metadata || !right.metadata) return 0;
  return compareSemanticVersions(
    left.metadata.releaseVersion,
    right.metadata.releaseVersion,
  );
}

function fullVersionKey(runtimeId: string, version: string) {
  return `${runtimeId.toLowerCase()}\n${version}`;
}

function packageIdentityKey(metadata: VelopackPackageMetadata) {
  return [
    metadata.releaseVersion,
    metadata.targetRuntimeId,
    metadata.artifactKind,
  ].join("\n");
}

function unique(values: string[]) {
  return [...new Set(values.filter(Boolean))];
}
