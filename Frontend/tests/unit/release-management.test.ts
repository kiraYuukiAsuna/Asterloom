import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ReleaseArtifactKindObject,
  ReleaseArtifactStatusObject,
} from "@/lib/api/generated/models";
import { uploadReleaseArtifactTransfer } from "@/lib/api/release-management";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("release artifact transfer", () => {
  it("preserves signed headers when a normalized upload is parsed again", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const id = "00000000-0000-4000-8000-000000000001";
    const timestamp = "2026-08-31T12:00:00Z";

    await uploadReleaseArtifactTransfer(
      "unused-for-direct-transfer",
      {
        artifact: {
          applicationId: id,
          archivedAt: null,
          artifactKind: ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL,
          contentType: "application/octet-stream",
          createdAt: timestamp,
          deltaFromVersion: "",
          environmentId: id,
          failureReason: "",
          fileName: "artifact.bin",
          id,
          releaseVersion: "1.0.0",
          sha256: "a".repeat(64),
          signature: "A".repeat(300),
          signingKeyId: id,
          sizeBytes: 3,
          status: ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_UPLOADING,
          storageBucketId: id,
          storageObjectId: id,
          storageObjectVersion: 1,
          targetRuntimeId: "win-x64",
          tenantId: id,
          updatedAt: timestamp,
          uploadSessionId: id,
          verifiedAt: null,
          version: 1,
        },
        uploadSession: {
          id,
          transfer: {
            expiresAt: timestamp,
            method: "PUT",
            requiredHeaders: {
              "content-type": "application/octet-stream",
              "x-amz-meta-asterloom-sha256": "abc123",
              "x-amz-meta-asterloom-size": "3",
            },
            url: "https://storage.example.test/release",
          },
        },
      },
      new File(["abc"], "artifact.bin", {
        type: "application/octet-stream",
      }),
    );

    const request = fetchMock.mock.calls[0]?.[1] as RequestInit;
    const headers = request.headers as Headers;
    expect(Object.fromEntries(headers.entries())).toEqual({
      "content-type": "application/octet-stream",
      "x-amz-meta-asterloom-sha256": "abc123",
      "x-amz-meta-asterloom-size": "3",
    });
  });
});
