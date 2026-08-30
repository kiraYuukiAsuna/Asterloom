import { z } from "zod";

import {
  DesktopReleaseStatusObject,
  ReleaseArtifactKindObject,
  ReleaseArtifactStatusObject,
  ReleaseChannelStatusObject,
  ReleaseSigningKeyStatusObject,
  ReleaseValidationSeverityObject,
  UpdateDecisionReasonObject,
  type EvaluationContext,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";
import {
  uploadStorageTransfer,
  type StorageTransferTicket,
} from "./storage-management";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const positiveVersionSchema = z.number().int().positive().safe();
const safeByteCountSchema = z.number().int().positive().safe();
const stableKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const runtimeIdPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const semanticVersionPattern =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;
const contentTypePattern = /^[a-z0-9!#$&^_.+-]+\/[a-z0-9!#$&^_.+*-]+$/;
const signaturePattern = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

export const releaseScopeSchema = z.object({
  applicationId: idSchema,
  environmentId: idSchema,
  tenantId: idSchema,
});
export const releaseKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(
    stableKeyPattern,
    "Use 1–100 lowercase letters, numbers, periods, underscores, or hyphens.",
  );
export const releaseDisplayNameSchema = z.string().trim().min(1).max(200);
export const releaseDescriptionSchema = z.string().trim().max(1_000);
export const releaseNotesSchema = z.string().trim().max(50_000);
export const releaseSemanticVersionSchema = z
  .string()
  .trim()
  .regex(semanticVersionPattern, "Enter a valid Semantic Version such as 1.2.3.");
export const releaseRuntimeIdSchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(runtimeIdPattern, "Enter a .NET runtime identifier such as win-x64.");
export const releaseFileNameSchema = z
  .string()
  .trim()
  .min(1)
  .max(255)
  .refine(
    (value) => !value.includes("/") && !value.includes("\\") && value !== "." && value !== "..",
    "Use a file name without path separators.",
  );
export const releaseContentTypeSchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(contentTypePattern, "Enter a valid media type.");
export const releaseSignatureSchema = z
  .string()
  .trim()
  .min(300, "Enter the Base64 RSA-PSS detached signature.")
  .max(1_400)
  .regex(signaturePattern, "Enter a valid Base64 detached signature.");
export const releasePublicKeyPemSchema = z
  .string()
  .trim()
  .min(200)
  .max(16_384)
  .refine(
    (value) =>
      value.includes("-----BEGIN PUBLIC KEY-----") &&
      value.includes("-----END PUBLIC KEY-----") &&
      !value.toUpperCase().includes("PRIVATE KEY"),
    "Enter an RSA public key in SubjectPublicKeyInfo PEM format.",
  );

export const releaseSigningKeyStatusSchema = z.enum([
  ReleaseSigningKeyStatusObject.RELEASE_SIGNING_KEY_STATUS_ACTIVE,
  ReleaseSigningKeyStatusObject.RELEASE_SIGNING_KEY_STATUS_ARCHIVED,
]);
export const releaseChannelStatusSchema = z.enum([
  ReleaseChannelStatusObject.RELEASE_CHANNEL_STATUS_ACTIVE,
  ReleaseChannelStatusObject.RELEASE_CHANNEL_STATUS_ARCHIVED,
]);
export const releaseArtifactStatusSchema = z.enum([
  ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_UPLOADING,
  ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_VERIFIED,
  ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_REJECTED,
  ReleaseArtifactStatusObject.RELEASE_ARTIFACT_STATUS_ARCHIVED,
]);
export const releaseArtifactKindSchema = z.enum([
  ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_FULL,
  ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA,
]);
export const desktopReleaseStatusSchema = z.enum([
  DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_DRAFT,
  DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_PUBLISHED,
  DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_PAUSED,
  DesktopReleaseStatusObject.DESKTOP_RELEASE_STATUS_ROLLED_BACK,
]);

const optionalIdSchema = z
  .string()
  .nullish()
  .transform((value, context) => {
    if (!value) return null;
    const parsed = idSchema.safeParse(value);
    if (!parsed.success) {
      context.addIssue({ code: "custom", message: "The API returned an invalid identifier." });
      return z.NEVER;
    }
    return parsed.data;
  });
const optionalTimestampSchema = timestampSchema.nullish().transform((value) => value ?? null);
const optionalTextSchema = z.string().nullish().transform((value) => value ?? "");

const signingKeySchema = z.object({
  algorithm: z.string().min(1),
  archivedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  displayName: releaseDisplayNameSchema,
  fingerprint: z.string().regex(/^[a-f0-9]{64}$/),
  id: idSchema,
  key: releaseKeySchema,
  publicKeyPem: z.string().min(1),
  status: releaseSigningKeyStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});
const channelSchema = z.object({
  activeReleaseId: optionalIdSchema,
  applicationId: idSchema,
  archivedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  description: optionalTextSchema,
  displayName: releaseDisplayNameSchema,
  environmentId: idSchema,
  id: idSchema,
  key: releaseKeySchema,
  previousReleaseId: optionalIdSchema,
  status: releaseChannelStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});
const artifactSchema = z.object({
  applicationId: idSchema,
  archivedAt: optionalTimestampSchema,
  artifactKind: releaseArtifactKindSchema,
  contentType: releaseContentTypeSchema,
  createdAt: timestampSchema,
  deltaFromVersion: optionalTextSchema,
  environmentId: idSchema,
  failureReason: optionalTextSchema,
  fileName: releaseFileNameSchema,
  id: idSchema,
  releaseVersion: releaseSemanticVersionSchema,
  sha256: z.string().regex(/^[a-f0-9]{64}$/),
  signature: releaseSignatureSchema,
  signingKeyId: idSchema,
  sizeBytes: safeByteCountSchema,
  status: releaseArtifactStatusSchema,
  storageBucketId: idSchema,
  storageObjectId: idSchema,
  storageObjectVersion: positiveVersionSchema,
  targetRuntimeId: releaseRuntimeIdSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  uploadSessionId: idSchema,
  verifiedAt: optionalTimestampSchema,
  version: positiveVersionSchema,
});
const wireHeadersSchema = z
  .object({ additionalData: z.record(z.string(), z.string()).optional() })
  .nullish()
  .transform((value) => value?.additionalData ?? {});
const transferTicketSchema = z.object({
  expiresAt: timestampSchema,
  method: z.enum(["GET", "PUT"]),
  requiredHeaders: wireHeadersSchema,
  url: z.string().min(1),
});
const uploadSessionSchema = z.object({
  id: idSchema,
  transfer: transferTicketSchema,
});
const artifactUploadSchema = z.object({
  artifact: artifactSchema,
  uploadSession: uploadSessionSchema,
});
const manifestArtifactSchema = z.object({
  artifactId: idSchema,
  artifactKind: releaseArtifactKindSchema,
  contentType: releaseContentTypeSchema,
  deltaFromVersion: optionalTextSchema,
  fileName: releaseFileNameSchema,
  sha256: z.string().regex(/^[a-f0-9]{64}$/),
  signature: releaseSignatureSchema,
  signingKeyFingerprint: z.string().regex(/^[a-f0-9]{64}$/),
  signingKeyId: idSchema,
  sizeBytes: safeByteCountSchema,
  targetRuntimeId: releaseRuntimeIdSchema,
});
const manifestSchema = z.object({
  artifacts: z.array(manifestArtifactSchema).nullish().transform((value) => value ?? []),
  channelKey: releaseKeySchema,
  displayName: releaseDisplayNameSchema,
  generatedAt: timestampSchema,
  mandatory: z.boolean(),
  minimumVersion: releaseSemanticVersionSchema,
  payloadJson: z.string().min(1),
  releaseId: idSchema,
  releaseNotes: optionalTextSchema,
  releaseVersion: releaseSemanticVersionSchema,
  revision: positiveVersionSchema,
  sha256: z.string().regex(/^[a-f0-9]{64}$/),
  signature: optionalTextSchema,
  signingKeyFingerprint: optionalTextSchema,
  signingKeyId: optionalIdSchema,
});
const desktopReleaseSchema = z.object({
  applicationId: idSchema,
  artifactIds: z.array(idSchema).nullish().transform((value) => value ?? []),
  bucketingSalt: z.string().min(1),
  channelId: idSchema,
  createdAt: timestampSchema,
  displayName: releaseDisplayNameSchema,
  environmentId: idSchema,
  id: idSchema,
  mandatory: z.boolean(),
  manifestGeneratedAt: optionalTimestampSchema,
  manifestPayloadJson: optionalTextSchema,
  manifestSha256: optionalTextSchema,
  manifestSignature: optionalTextSchema,
  manifestSigningKeyFingerprint: optionalTextSchema,
  manifestSigningKeyId: optionalIdSchema,
  minimumVersion: releaseSemanticVersionSchema,
  pausedAt: optionalTimestampSchema,
  publishedAt: optionalTimestampSchema,
  releaseNotes: optionalTextSchema,
  releaseVersion: releaseSemanticVersionSchema,
  revision: positiveVersionSchema,
  rolledBackAt: optionalTimestampSchema,
  rolloutBasisPoints: z.number().int().min(1).max(100_000),
  status: desktopReleaseStatusSchema,
  targetSegmentId: optionalIdSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});
const validationIssueSchema = z.object({
  code: z.string().min(1),
  message: z.string().min(1),
  path: z.string(),
  severity: z.enum([
    ReleaseValidationSeverityObject.RELEASE_VALIDATION_SEVERITY_ERROR,
    ReleaseValidationSeverityObject.RELEASE_VALIDATION_SEVERITY_WARNING,
  ]),
});
const validationSchema = z.object({
  candidateManifest: manifestSchema.nullish().transform((value) => value ?? null),
  issues: z.array(validationIssueSchema).nullish().transform((value) => value ?? []),
  valid: z.boolean(),
});
const decisionSchema = z.object({
  bucket: z.number().int().nonnegative(),
  bucketEvaluated: z.boolean(),
  channel: channelSchema.nullish().transform((value) => value ?? null),
  mandatory: z.boolean(),
  reason: z.enum([
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_UPDATE_AVAILABLE,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_CURRENT,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_CHANNEL_EMPTY,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_RELEASE_PAUSED,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_TARGETING_MISS,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_ROLLOUT_EXCLUDED,
    UpdateDecisionReasonObject.UPDATE_DECISION_REASON_NO_COMPATIBLE_ARTIFACT,
  ]),
  release: desktopReleaseSchema.nullish().transform((value) => value ?? null),
  rolloutBasisPoints: z.number().int().nonnegative(),
  selectedArtifact: artifactSchema.nullish().transform((value) => value ?? null),
  trace: z.array(z.string()).nullish().transform((value) => value ?? []),
  updateAvailable: z.boolean(),
});

const pageSchema = z.object({
  pageSize: z.number().int().min(1).max(100).default(25),
  pageToken: z.string().max(2_048).default(""),
  query: z.string().trim().max(200).default(""),
});
const signingKeysPageSchema = z.object({
  nextPageToken: optionalTextSchema,
  signingKeys: z.array(signingKeySchema).nullish().transform((value) => value ?? []),
});
const channelsPageSchema = z.object({
  channels: z.array(channelSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const artifactsPageSchema = z.object({
  artifacts: z.array(artifactSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const releasesPageSchema = z.object({
  nextPageToken: optionalTextSchema,
  releases: z.array(desktopReleaseSchema).nullish().transform((value) => value ?? []),
});
const releaseDraftInputSchema = z.object({
  artifactIds: z.array(idSchema).min(1).max(100),
  displayName: releaseDisplayNameSchema,
  mandatory: z.boolean(),
  minimumVersion: releaseSemanticVersionSchema,
  releaseNotes: releaseNotesSchema,
  rolloutBasisPoints: z.number().int().min(1).max(100_000),
  targetSegmentId: optionalIdSchema,
});

export type ReleaseScope = z.infer<typeof releaseScopeSchema>;
export type ReleaseSigningKeyRecord = z.infer<typeof signingKeySchema>;
export type ReleaseChannelRecord = z.infer<typeof channelSchema>;
export type ReleaseArtifactRecord = z.infer<typeof artifactSchema>;
export type ReleaseArtifactUploadRecord = z.infer<typeof artifactUploadSchema>;
export type DesktopReleaseRecord = z.infer<typeof desktopReleaseSchema>;
export type ReleaseManifestRecord = z.infer<typeof manifestSchema>;
export type ReleaseValidationRecord = z.infer<typeof validationSchema>;
export type ReleaseDecisionRecord = z.infer<typeof decisionSchema>;
export type ReleaseArtifactKind = z.infer<typeof releaseArtifactKindSchema>;

export async function listReleaseSigningKeys(
  tenantId: string,
  options: z.input<typeof pageSchema> & { includeArchived?: boolean },
) {
  const page = pageSchema.parse(options);
  const response = await signingKeysBuilder(idSchema.parse(tenantId)).get({
    queryParameters: {
      ...page,
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
    },
  });
  return signingKeysPageSchema.parse(requireResponse(response));
}

export async function createReleaseSigningKey(
  csrfToken: string,
  tenantId: string,
  input: { displayName: string; key: string; publicKeyPem: string },
) {
  const parsed = z
    .object({
      displayName: releaseDisplayNameSchema,
      key: releaseKeySchema,
      publicKeyPem: releasePublicKeyPemSchema,
    })
    .parse(input);
  const response = await signingKeysBuilder(idSchema.parse(tenantId), csrfToken).post(parsed);
  return signingKeySchema.parse(requireResponse(response));
}

export async function archiveReleaseSigningKey(
  csrfToken: string,
  signingKey: ReleaseSigningKeyRecord,
) {
  const current = signingKeySchema.parse(signingKey);
  const response = await signingKeysBuilder(current.tenantId, csrfToken)
    .bySigningKeyId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return signingKeySchema.parse(requireResponse(response));
}

export async function restoreReleaseSigningKey(
  csrfToken: string,
  signingKey: ReleaseSigningKeyRecord,
) {
  const current = signingKeySchema.parse(signingKey);
  const response = await signingKeysBuilder(current.tenantId, csrfToken)
    .withSigningKeyIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return signingKeySchema.parse(requireResponse(response));
}

export async function listReleaseChannels(
  scope: ReleaseScope,
  options: z.input<typeof pageSchema> & { includeArchived?: boolean },
) {
  const parsedScope = releaseScopeSchema.parse(scope);
  const page = pageSchema.parse(options);
  const response = await channelsBuilder(parsedScope).get({
    queryParameters: {
      ...page,
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
    },
  });
  return channelsPageSchema.parse(requireResponse(response));
}

export async function getReleaseChannel(scope: ReleaseScope, channelId: string) {
  const response = await channelsBuilder(releaseScopeSchema.parse(scope))
    .byChannelId(idSchema.parse(channelId))
    .get();
  return channelSchema.parse(requireResponse(response));
}

export async function createReleaseChannel(
  csrfToken: string,
  scope: ReleaseScope,
  input: { description: string; displayName: string; key: string },
) {
  const parsed = z
    .object({
      description: releaseDescriptionSchema,
      displayName: releaseDisplayNameSchema,
      key: releaseKeySchema,
    })
    .parse(input);
  const response = await channelsBuilder(releaseScopeSchema.parse(scope), csrfToken).post(parsed);
  return channelSchema.parse(requireResponse(response));
}

export async function updateReleaseChannel(
  csrfToken: string,
  channel: ReleaseChannelRecord,
  input: { description: string; displayName: string },
) {
  const current = channelSchema.parse(channel);
  const parsed = z
    .object({
      description: releaseDescriptionSchema,
      displayName: releaseDisplayNameSchema,
    })
    .parse(input);
  const response = await channelsBuilder(current, csrfToken)
    .byChannelId(current.id)
    .patch({ ...parsed, expectedVersion: current.version });
  return channelSchema.parse(requireResponse(response));
}

export async function archiveReleaseChannel(
  csrfToken: string,
  channel: ReleaseChannelRecord,
) {
  const current = channelSchema.parse(channel);
  const response = await channelsBuilder(current, csrfToken)
    .byChannelId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return channelSchema.parse(requireResponse(response));
}

export async function restoreReleaseChannel(
  csrfToken: string,
  channel: ReleaseChannelRecord,
) {
  const current = channelSchema.parse(channel);
  const response = await channelsBuilder(current, csrfToken)
    .withChannelIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return channelSchema.parse(requireResponse(response));
}

export async function listReleaseArtifacts(
  scope: ReleaseScope,
  options: z.input<typeof pageSchema> & { includeArchived?: boolean },
) {
  const parsedScope = releaseScopeSchema.parse(scope);
  const page = pageSchema.parse(options);
  const response = await artifactsBuilder(parsedScope).get({
    queryParameters: {
      ...page,
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
    },
  });
  return artifactsPageSchema.parse(requireResponse(response));
}

export async function getReleaseArtifact(scope: ReleaseScope, artifactId: string) {
  const response = await artifactsBuilder(releaseScopeSchema.parse(scope))
    .byArtifactId(idSchema.parse(artifactId))
    .get();
  return artifactSchema.parse(requireResponse(response));
}

export async function createReleaseArtifactUpload(
  csrfToken: string,
  scope: ReleaseScope,
  input: {
    artifactKind: ReleaseArtifactKind;
    contentType: string;
    deltaFromVersion?: string;
    fileName: string;
    releaseVersion: string;
    sha256: string;
    signature: string;
    signingKeyId: string;
    sizeBytes: number;
    targetRuntimeId: string;
  },
) {
  const parsed = z
    .object({
      artifactKind: releaseArtifactKindSchema,
      contentType: releaseContentTypeSchema,
      deltaFromVersion: z.string().trim().default(""),
      fileName: releaseFileNameSchema,
      releaseVersion: releaseSemanticVersionSchema,
      sha256: z.string().trim().toLowerCase().regex(/^[a-f0-9]{64}$/),
      signature: releaseSignatureSchema,
      signingKeyId: idSchema,
      sizeBytes: safeByteCountSchema.max(4 * 1024 * 1024 * 1024),
      targetRuntimeId: releaseRuntimeIdSchema,
    })
    .superRefine((value, context) => {
      const isDelta =
        value.artifactKind === ReleaseArtifactKindObject.RELEASE_ARTIFACT_KIND_DELTA;
      if (isDelta && !semanticVersionPattern.test(value.deltaFromVersion)) {
        context.addIssue({
          code: "custom",
          message: "Delta artifacts require an older source Semantic Version.",
          path: ["deltaFromVersion"],
        });
      }
      if (!isDelta && value.deltaFromVersion) {
        context.addIssue({
          code: "custom",
          message: "Full artifacts cannot specify a delta source version.",
          path: ["deltaFromVersion"],
        });
      }
    })
    .parse(input);
  const response = await environmentBuilder(releaseScopeSchema.parse(scope), csrfToken)
    .release.artifactsBeginUpload.post(parsed);
  return artifactUploadSchema.parse(requireResponse(response));
}

export async function uploadReleaseArtifactTransfer(
  csrfToken: string,
  upload: ReleaseArtifactUploadRecord,
  file: File,
) {
  const current = artifactUploadSchema.parse(upload);
  await uploadStorageTransfer(
    csrfToken,
    current.uploadSession.transfer as StorageTransferTicket,
    file,
  );
}

export async function completeReleaseArtifactUpload(
  csrfToken: string,
  artifact: ReleaseArtifactRecord,
) {
  const current = artifactSchema.parse(artifact);
  const response = await artifactsBuilder(current, csrfToken)
    .withArtifactIdComplete(current.id)
    .post({ expectedVersion: current.version });
  return artifactSchema.parse(requireResponse(response));
}

export async function archiveReleaseArtifact(
  csrfToken: string,
  artifact: ReleaseArtifactRecord,
) {
  const current = artifactSchema.parse(artifact);
  const response = await artifactsBuilder(current, csrfToken)
    .byArtifactId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return artifactSchema.parse(requireResponse(response));
}

export async function listDesktopReleases(
  scope: ReleaseScope,
  options: z.input<typeof pageSchema> & { includeInactive?: boolean },
) {
  const parsedScope = releaseScopeSchema.parse(scope);
  const page = pageSchema.parse(options);
  const response = await releasesBuilder(parsedScope).get({
    queryParameters: {
      ...page,
      includeInactive: z.boolean().default(false).parse(options.includeInactive),
    },
  });
  return releasesPageSchema.parse(requireResponse(response));
}

export async function getDesktopRelease(scope: ReleaseScope, releaseId: string) {
  const response = await releasesBuilder(releaseScopeSchema.parse(scope))
    .byReleaseId(idSchema.parse(releaseId))
    .get();
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function createDesktopRelease(
  csrfToken: string,
  scope: ReleaseScope,
  input: z.input<typeof releaseDraftInputSchema> & {
    channelId: string;
    releaseVersion: string;
  },
) {
  const parsed = releaseDraftInputSchema
    .extend({ channelId: idSchema, releaseVersion: releaseSemanticVersionSchema })
    .parse(input);
  const response = await releasesBuilder(releaseScopeSchema.parse(scope), csrfToken).post({
    ...parsed,
    targetSegmentId: parsed.targetSegmentId ?? "",
  });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function updateDesktopReleaseDraft(
  csrfToken: string,
  release: DesktopReleaseRecord,
  input: z.input<typeof releaseDraftInputSchema>,
) {
  const current = desktopReleaseSchema.parse(release);
  const parsed = releaseDraftInputSchema.parse(input);
  const response = await releasesBuilder(current, csrfToken)
    .byReleaseId(current.id)
    .patch({
      ...parsed,
      expectedVersion: current.version,
      targetSegmentId: parsed.targetSegmentId ?? "",
    });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function validateDesktopRelease(
  csrfToken: string,
  release: DesktopReleaseRecord,
) {
  const current = desktopReleaseSchema.parse(release);
  const response = await releasesBuilder(current, csrfToken)
    .withReleaseIdValidate(current.id)
    .post({});
  return validationSchema.parse(requireResponse(response));
}

export async function publishDesktopRelease(
  csrfToken: string,
  release: DesktopReleaseRecord,
  channel: ReleaseChannelRecord,
  manifestSigningKeyId: string,
  manifestSignature: string,
) {
  const current = desktopReleaseSchema.parse(release);
  const currentChannel = channelSchema.parse(channel);
  if (current.channelId !== currentChannel.id) {
    throw new Error("The selected channel does not belong to this release.");
  }
  const response = await releasesBuilder(current, csrfToken)
    .withReleaseIdPublish(current.id)
    .post({
      expectedChannelVersion: currentChannel.version,
      expectedVersion: current.version,
      manifestSignature: releaseSignatureSchema.parse(manifestSignature),
      manifestSigningKeyId: idSchema.parse(manifestSigningKeyId),
    });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function pauseDesktopRelease(
  csrfToken: string,
  release: DesktopReleaseRecord,
) {
  const current = desktopReleaseSchema.parse(release);
  const response = await releasesBuilder(current, csrfToken)
    .withReleaseIdPause(current.id)
    .post({ expectedVersion: current.version });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function promoteDesktopRelease(
  csrfToken: string,
  release: DesktopReleaseRecord,
  rolloutBasisPoints: number,
) {
  const current = desktopReleaseSchema.parse(release);
  const response = await releasesBuilder(current, csrfToken)
    .withReleaseIdPromote(current.id)
    .post({
      expectedVersion: current.version,
      rolloutBasisPoints: z.number().int().min(1).max(100_000).parse(rolloutBasisPoints),
    });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function rollbackDesktopRelease(
  csrfToken: string,
  release: DesktopReleaseRecord,
  target: DesktopReleaseRecord,
  channel: ReleaseChannelRecord,
) {
  const current = desktopReleaseSchema.parse(release);
  const targetRelease = desktopReleaseSchema.parse(target);
  const currentChannel = channelSchema.parse(channel);
  const response = await releasesBuilder(current, csrfToken)
    .withReleaseIdRollback(current.id)
    .post({
      expectedChannelVersion: currentChannel.version,
      expectedTargetVersion: targetRelease.version,
      expectedVersion: current.version,
      targetReleaseId: targetRelease.id,
    });
  return desktopReleaseSchema.parse(requireResponse(response));
}

export async function getReleaseManifest(release: DesktopReleaseRecord) {
  const current = desktopReleaseSchema.parse(release);
  const response = await releasesBuilder(current).byReleaseId(current.id).manifest.get();
  return manifestSchema.parse(requireResponse(response));
}

export async function simulateReleaseUpdate(
  csrfToken: string,
  scope: ReleaseScope,
  input: {
    channelKey: string;
    clientVersion?: string;
    currentVersion: string;
    region?: string;
    targetRuntimeId: string;
    targetingKey: string;
    userId?: string;
  },
) {
  const parsed = z
    .object({
      channelKey: releaseKeySchema,
      clientVersion: z.string().trim().max(100).default(""),
      currentVersion: releaseSemanticVersionSchema,
      region: z.string().trim().max(100).default(""),
      targetRuntimeId: releaseRuntimeIdSchema,
      targetingKey: z.string().trim().min(1).max(512),
      userId: z.string().trim().max(200).default(""),
    })
    .parse(input);
  const context: EvaluationContext = {
    attributes: [],
    clientVersion: parsed.clientVersion,
    language: "",
    platform: parsed.targetRuntimeId.split("-")[0] ?? "",
    region: parsed.region,
    targetingKey: parsed.targetingKey,
    userId: parsed.userId,
  };
  const response = await environmentBuilder(releaseScopeSchema.parse(scope), csrfToken)
    .releaseSimulate.post({
      channelKey: parsed.channelKey,
      context,
      currentVersion: parsed.currentVersion,
      targetRuntimeId: parsed.targetRuntimeId,
    });
  return decisionSchema.parse(requireResponse(response));
}

export function releaseErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted release values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The release operation could not be completed.";
}

function environmentBuilder(scope: ReleaseScope, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(scope.tenantId)
    .applications.byApplicationId(scope.applicationId)
    .environments.byEnvironmentId(scope.environmentId);
}

function signingKeysBuilder(tenantId: string, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(tenantId)
    .release.signingKeys;
}

function channelsBuilder(scope: ReleaseScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).release.channels;
}

function artifactsBuilder(scope: ReleaseScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).release.artifacts;
}

function releasesBuilder(scope: ReleaseScope, csrfToken?: string) {
  return environmentBuilder(scope, csrfToken).releases;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Release API returned an empty response.");
  return response;
}
