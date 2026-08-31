import { strToU8, zipSync } from "fflate";
import { describe, expect, it } from "vitest";

import { ReleaseArtifactKindObject } from "@/lib/api/generated/models";
import {
  compareSemanticVersions,
  inspectVelopackPackage,
  readVelopackSigningBundle,
} from "@/lib/release/velopack-package";

describe("Velopack package inspection", () => {
  it("reads compressed NuSpec metadata and derives the artifact kind", async () => {
    const file = createPackage(
      "Asterloom.Sample-1.2.0-stable-full.nupkg",
      "1.2.0",
      "stable",
      "win-x64",
    );

    await expect(inspectVelopackPackage(file)).resolves.toEqual({
      artifactKind: ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL,
      channel: "stable",
      packageId: "Asterloom.Sample",
      releaseVersion: "1.2.0",
      targetRuntimeId: "win-x64",
    });
  });

  it("recognizes delta packages without asking the operator for metadata", async () => {
    const file = createPackage(
      "Asterloom.Sample-2.0.0-beta-delta.nupkg",
      "2.0.0-beta.1",
      "beta",
      "linux-arm64",
    );

    const metadata = await inspectVelopackPackage(file);
    expect(metadata.artifactKind).toBe(
      ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA,
    );
    expect(metadata.releaseVersion).toBe("2.0.0-beta.1");
    expect(metadata.targetRuntimeId).toBe("linux-arm64");
  });

  it("rejects a NuSpec whose package ID does not match its root file name", async () => {
    const archive = zipSync({
      "Different.Package.nuspec": strToU8(nuspec("1.0.0", "stable", "win-x64")),
    });
    const file = fileFromBytes(
      archive,
      "Asterloom.Sample-1.0.0-stable-full.nupkg",
    );

    await expect(inspectVelopackPackage(file)).rejects.toThrow(
      "does not match the NuSpec file name",
    );
  });
});

describe("Velopack signing bundle", () => {
  it("normalizes the fingerprint and artifact hash", async () => {
    const file = new File(
      [
        JSON.stringify({
          algorithm: "RSA-PSS-SHA256",
          artifacts: {
            "Asterloom.Sample-1.0.0-stable-full.nupkg": {
              sha256: "A".repeat(64),
              signature: "A".repeat(344),
            },
          },
          fingerprint: "B".repeat(64),
        }),
      ],
      "signing-metadata.json",
      { type: "application/json" },
    );

    const bundle = await readVelopackSigningBundle(file);
    expect(bundle.fingerprint).toBe("b".repeat(64));
    expect(
      bundle.artifacts["Asterloom.Sample-1.0.0-stable-full.nupkg"]?.sha256,
    ).toBe("a".repeat(64));
  });
});

describe("Semantic Version ordering", () => {
  it("orders prereleases, releases, and numeric identifiers", () => {
    expect(compareSemanticVersions("1.0.0-beta.2", "1.0.0-beta.11")).toBeLessThan(0);
    expect(compareSemanticVersions("1.0.0-alpha-beta", "1.0.0-alpha-gamma")).toBeLessThan(0);
    expect(compareSemanticVersions("1.0.0-rc.1", "1.0.0")).toBeLessThan(0);
    expect(compareSemanticVersions("2.0.0+build.1", "2.0.0+build.2")).toBe(0);
  });
});

function createPackage(
  fileName: string,
  version: string,
  channel: string,
  runtimeId: string,
) {
  return fileFromBytes(
    zipSync({ "Asterloom.Sample.nuspec": strToU8(nuspec(version, channel, runtimeId)) }),
    fileName,
  );
}

function nuspec(version: string, channel: string, runtimeId: string) {
  return `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Asterloom.Sample</id>
    <version>${version}</version>
    <authors>Asterloom</authors>
    <description>Test package</description>
    <channel>${channel}</channel>
    <rid>${runtimeId}</rid>
  </metadata>
</package>`;
}

function fileFromBytes(bytes: Uint8Array, name: string) {
  const buffer = bytes.buffer.slice(
    bytes.byteOffset,
    bytes.byteOffset + bytes.byteLength,
  ) as ArrayBuffer;
  return new File([buffer], name, { type: "application/octet-stream" });
}
