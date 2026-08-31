import { inflateSync } from "fflate";
import { z } from "zod";

import { ReleaseArtifactKindObject } from "@/lib/api/generated/models";
import {
  releaseRuntimeIdSchema,
  releaseSemanticVersionSchema,
  type ReleaseArtifactKind,
} from "@/lib/api/release-management";

const localFileHeaderSignature = 0x04034b50;
const centralDirectoryHeaderSignature = 0x02014b50;
const localFileHeaderLength = 30;
const maximumEntriesBeforeNuspec = 64;
const maximumBytesBeforeNuspec = 16 * 1024 * 1024;
const maximumNuspecCompressedBytes = 4 * 1024 * 1024;
const maximumNuspecBytes = 1024 * 1024;
const packageIdPattern = /^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,198}[A-Za-z0-9])?$/;
const stableKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const artifactFileNamePattern = /-(full|delta)\.nupkg$/i;
const sha256Pattern = /^[a-f0-9]{64}$/;
const base64Pattern = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

export type VelopackPackageMetadata = {
  artifactKind: ReleaseArtifactKind;
  channel: string;
  packageId: string;
  releaseVersion: string;
  targetRuntimeId: string;
};

export type VelopackSigningArtifact = {
  sha256: string;
  signature: string;
};

export type VelopackSigningBundle = {
  algorithm: "RSA-PSS-SHA256";
  artifacts: Record<string, VelopackSigningArtifact>;
  fingerprint: string;
};

const signingArtifactSchema = z.object({
  sha256: z.string().trim().toLowerCase().regex(sha256Pattern),
  signature: z.string().trim().min(300).max(1_400).regex(base64Pattern),
});

const signingBundleSchema = z
  .object({
    algorithm: z.literal("RSA-PSS-SHA256"),
    artifacts: z.record(z.string().min(1), signingArtifactSchema),
    fingerprint: z.string().trim().toLowerCase().regex(sha256Pattern),
  })
  .transform((value) => ({
    algorithm: value.algorithm,
    artifacts: Object.fromEntries(
      Object.entries(value.artifacts).map(([fileName, artifact]) => [
        fileName.trim(),
        artifact,
      ]),
    ),
    fingerprint: value.fingerprint,
  }));

export async function inspectVelopackPackage(
  file: File,
): Promise<VelopackPackageMetadata> {
  if (file.size === 0) {
    throw new Error("The Velopack package is empty.");
  }

  const kindMatch = artifactFileNamePattern.exec(file.name.trim());
  if (!kindMatch) {
    throw new Error(
      "A Velopack package file name must end in -full.nupkg or -delta.nupkg.",
    );
  }
  const artifactKind =
    kindMatch[1]?.toLowerCase() === "full"
      ? ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL
      : ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA;

  let offset = 0;
  for (let index = 0; index < maximumEntriesBeforeNuspec; index += 1) {
    if (offset > maximumBytesBeforeNuspec) break;
    const header = await readSlice(file, offset, localFileHeaderLength);
    const view = new DataView(header.buffer, header.byteOffset, header.byteLength);
    const signature = view.getUint32(0, true);
    if (signature === centralDirectoryHeaderSignature) {
      throw new Error("The package does not contain a root NuSpec file.");
    }
    if (signature !== localFileHeaderSignature) {
      throw new Error("The package is not a supported ZIP archive.");
    }

    const flags = view.getUint16(6, true);
    const compressionMethod = view.getUint16(8, true);
    const compressedSize = view.getUint32(18, true);
    const uncompressedSize = view.getUint32(22, true);
    const fileNameLength = view.getUint16(26, true);
    const extraFieldLength = view.getUint16(28, true);
    if ((flags & 0x0001) !== 0) {
      throw new Error("Encrypted Velopack packages are not supported.");
    }
    if (
      (flags & 0x0008) !== 0 ||
      compressedSize === 0xffffffff ||
      uncompressedSize === 0xffffffff
    ) {
      throw new Error(
        "The Velopack NuSpec must use ordinary ZIP sizes without a data descriptor.",
      );
    }
    if (fileNameLength === 0 || fileNameLength > 4_096) {
      throw new Error("The package contains an invalid ZIP entry header.");
    }

    const entryNameBytes = await readSlice(
      file,
      offset + localFileHeaderLength,
      fileNameLength,
    );
    const entryName = new TextDecoder().decode(entryNameBytes).replaceAll("\\", "/");
    const payloadOffset =
      offset + localFileHeaderLength + fileNameLength + extraFieldLength;
    if (!entryName.includes("/") && entryName.toLowerCase().endsWith(".nuspec")) {
      if (
        compressedSize > maximumNuspecCompressedBytes ||
        uncompressedSize > maximumNuspecBytes
      ) {
        throw new Error("The Velopack NuSpec is too large.");
      }
      const compressed = await readSlice(file, payloadOffset, compressedSize);
      const xmlBytes = decompressNuspec(
        compressed,
        compressionMethod,
        uncompressedSize,
      );
      return parseNuspec(
        new TextDecoder().decode(xmlBytes),
        entryName,
        artifactKind,
      );
    }

    offset = payloadOffset + compressedSize;
    if (offset > maximumBytesBeforeNuspec) break;
  }

  throw new Error(
    "The package does not expose a root NuSpec near the start of the archive.",
  );
}

export async function readVelopackSigningBundle(
  file: File,
): Promise<VelopackSigningBundle> {
  if (file.size === 0 || file.size > 5 * 1024 * 1024) {
    throw new Error("The signing bundle must be a non-empty JSON file under 5 MiB.");
  }

  let value: unknown;
  try {
    value = JSON.parse(await file.text());
  } catch {
    throw new Error("The signing bundle is not valid JSON.");
  }

  const parsed = signingBundleSchema.safeParse(value);
  if (!parsed.success) {
    throw new Error(
      parsed.error.issues[0]?.message ?? "The signing bundle is invalid.",
    );
  }
  if (Object.keys(parsed.data.artifacts).length === 0) {
    throw new Error("The signing bundle does not contain any package signatures.");
  }
  return parsed.data;
}

export function compareSemanticVersions(left: string, right: string): number {
  const leftVersion = splitSemanticVersion(releaseSemanticVersionSchema.parse(left));
  const rightVersion = splitSemanticVersion(releaseSemanticVersionSchema.parse(right));
  for (let index = 0; index < 3; index += 1) {
    const comparison = compareNumericIdentifiers(
      leftVersion.core[index] ?? "0",
      rightVersion.core[index] ?? "0",
    );
    if (comparison !== 0) return comparison;
  }

  if (leftVersion.prerelease.length === 0 && rightVersion.prerelease.length === 0) {
    return 0;
  }
  if (leftVersion.prerelease.length === 0) return 1;
  if (rightVersion.prerelease.length === 0) return -1;

  const maximum = Math.max(
    leftVersion.prerelease.length,
    rightVersion.prerelease.length,
  );
  for (let index = 0; index < maximum; index += 1) {
    const leftIdentifier = leftVersion.prerelease[index];
    const rightIdentifier = rightVersion.prerelease[index];
    if (leftIdentifier === undefined) return -1;
    if (rightIdentifier === undefined) return 1;
    if (leftIdentifier === rightIdentifier) continue;

    const leftNumeric = /^\d+$/.test(leftIdentifier);
    const rightNumeric = /^\d+$/.test(rightIdentifier);
    if (leftNumeric && rightNumeric) {
      return compareNumericIdentifiers(leftIdentifier, rightIdentifier);
    }
    if (leftNumeric) return -1;
    if (rightNumeric) return 1;
    return leftIdentifier < rightIdentifier ? -1 : 1;
  }
  return 0;
}

async function readSlice(file: File, offset: number, length: number) {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || length < 0) {
    throw new Error("The package contains an invalid ZIP entry size.");
  }
  const end = offset + length;
  if (offset < 0 || end > file.size || end < offset) {
    throw new Error("The Velopack ZIP archive ended unexpectedly.");
  }
  return new Uint8Array(await file.slice(offset, end).arrayBuffer());
}

function decompressNuspec(
  compressed: Uint8Array,
  compressionMethod: number,
  expectedSize: number,
) {
  let result: Uint8Array;
  if (compressionMethod === 0) {
    result = compressed;
  } else if (compressionMethod === 8) {
    try {
      result = inflateSync(compressed);
    } catch {
      throw new Error("The Velopack NuSpec compressed data is invalid.");
    }
  } else {
    throw new Error(
      "The Velopack NuSpec uses an unsupported ZIP compression method.",
    );
  }

  if (result.byteLength > maximumNuspecBytes) {
    throw new Error("The Velopack NuSpec is too large.");
  }
  if (expectedSize !== 0 && result.byteLength !== expectedSize) {
    throw new Error("The Velopack NuSpec size does not match its ZIP header.");
  }
  return result;
}

function parseNuspec(
  xml: string,
  entryName: string,
  artifactKind: ReleaseArtifactKind,
): VelopackPackageMetadata {
  if (/<!DOCTYPE|<!ENTITY/i.test(xml)) {
    throw new Error("The Velopack NuSpec XML contains a prohibited declaration.");
  }

  const document = new DOMParser().parseFromString(xml, "application/xml");
  if (document.getElementsByTagName("parsererror").length > 0) {
    throw new Error("The Velopack NuSpec XML is invalid.");
  }
  const metadata =
    document.getElementsByTagNameNS("*", "metadata").item(0) ??
    document.getElementsByTagName("metadata").item(0);
  if (!metadata) {
    throw new Error("The NuSpec metadata element is missing.");
  }

  const packageId = requiredElementValue(metadata, "id");
  const releaseVersion = requiredElementValue(metadata, "version");
  const channel = requiredElementValue(metadata, "channel").toLowerCase();
  const targetRuntimeId = requiredElementValue(metadata, "rid").toLowerCase();
  const nuspecPackageId = entryName.slice(0, -".nuspec".length);
  if (
    !packageIdPattern.test(packageId) ||
    nuspecPackageId.toLowerCase() !== packageId.toLowerCase()
  ) {
    throw new Error(
      "The NuSpec package ID is invalid or does not match the NuSpec file name.",
    );
  }
  releaseSemanticVersionSchema.parse(releaseVersion);
  releaseRuntimeIdSchema.parse(targetRuntimeId);
  if (!stableKeyPattern.test(channel)) {
    throw new Error("The NuSpec channel is invalid.");
  }

  return {
    artifactKind,
    channel,
    packageId,
    releaseVersion,
    targetRuntimeId,
  };
}

function requiredElementValue(metadata: Element, name: string) {
  const element =
    metadata.getElementsByTagNameNS("*", name).item(0) ??
    metadata.getElementsByTagName(name).item(0);
  const value = element?.textContent?.trim();
  if (!value) {
    throw new Error(`The NuSpec ${name} value is required.`);
  }
  return value;
}

function splitSemanticVersion(version: string) {
  const withoutBuild = version.split("+", 1)[0] ?? version;
  const prereleaseSeparator = withoutBuild.indexOf("-");
  const core = prereleaseSeparator < 0
    ? withoutBuild
    : withoutBuild.slice(0, prereleaseSeparator);
  const prerelease = prereleaseSeparator < 0
    ? ""
    : withoutBuild.slice(prereleaseSeparator + 1);
  return {
    core: core.split("."),
    prerelease: prerelease ? prerelease.split(".") : [],
  };
}

function compareNumericIdentifiers(left: string, right: string) {
  const leftNormalized = left.replace(/^0+(?=\d)/, "");
  const rightNormalized = right.replace(/^0+(?=\d)/, "");
  if (leftNormalized.length !== rightNormalized.length) {
    return leftNormalized.length < rightNormalized.length ? -1 : 1;
  }
  if (leftNormalized === rightNormalized) return 0;
  return leftNormalized < rightNormalized ? -1 : 1;
}
