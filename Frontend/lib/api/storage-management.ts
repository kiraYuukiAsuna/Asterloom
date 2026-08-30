import { z } from "zod";

import {
  StorageAccessPolicyObject,
  StorageObjectStatusObject,
  StorageResourceStatusObject,
  StorageUploadStatusObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const positiveVersionSchema = z.number().int().positive().safe();
const byteCountSchema = z.number().int().nonnegative().safe();
const bucketKeyPattern = /^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$/;
const objectKeyPattern = /^[a-z0-9](?:[a-z0-9._/-]{0,510}[a-z0-9])?$/;
const metadataKeyPattern = /^[a-z0-9][a-z0-9._-]{0,62}$/;
const contentTypePattern = /^[a-z0-9!#$&^_.+-]+\/[a-z0-9!#$&^_.+*-]+$/;

export const storageAccessPolicySchema = z.enum([
  StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_PRIVATE,
  StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_AUTHENTICATED_READ,
]);
const storageResourceStatusSchema = z.enum([
  StorageResourceStatusObject.STORAGE_RESOURCE_STATUS_ACTIVE,
  StorageResourceStatusObject.STORAGE_RESOURCE_STATUS_ARCHIVED,
]);
const storageObjectStatusSchema = z.enum([
  StorageObjectStatusObject.STORAGE_OBJECT_STATUS_PENDING,
  StorageObjectStatusObject.STORAGE_OBJECT_STATUS_AVAILABLE,
  StorageObjectStatusObject.STORAGE_OBJECT_STATUS_FAILED,
  StorageObjectStatusObject.STORAGE_OBJECT_STATUS_DELETED,
]);
const storageUploadStatusSchema = z.enum([
  StorageUploadStatusObject.STORAGE_UPLOAD_STATUS_PENDING,
  StorageUploadStatusObject.STORAGE_UPLOAD_STATUS_UPLOADED,
  StorageUploadStatusObject.STORAGE_UPLOAD_STATUS_COMPLETED,
  StorageUploadStatusObject.STORAGE_UPLOAD_STATUS_FAILED,
  StorageUploadStatusObject.STORAGE_UPLOAD_STATUS_EXPIRED,
]);

export const storageBucketKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(
    bucketKeyPattern,
    "Use 1–100 lowercase letters, numbers, periods, underscores, or hyphens.",
  );
export const storageObjectKeySchema = z
  .string()
  .trim()
  .toLowerCase()
  .refine(
    (value) =>
      objectKeyPattern.test(value) && !value.includes("..") && !value.includes("//"),
    "Use a safe lowercase object path without empty or parent segments.",
  );
export const storageDisplayNameSchema = z.string().trim().min(1).max(200);
export const storageDescriptionSchema = z.string().trim().max(1_000);
export const storageFileNameSchema = z
  .string()
  .trim()
  .min(1)
  .max(255)
  .refine(
    (value) => !value.includes("/") && !value.includes("\\") && value !== "." && value !== "..",
    "Use a file name without path separators.",
  );
export const storageContentTypeSchema = z
  .string()
  .trim()
  .toLowerCase()
  .regex(contentTypePattern, "Enter a valid media type.");
export const storageMetadataSchema = z
  .record(
    z.string().trim().toLowerCase().regex(metadataKeyPattern),
    z.string().trim().max(1_000),
  )
  .refine((value) => Object.keys(value).length <= 32, "At most 32 metadata entries are allowed.");

const wireMetadataSchema = z.preprocess((value) => {
  if (value === null || value === undefined) return {};
  if (
    typeof value === "object" &&
    !Array.isArray(value) &&
    "additionalData" in value
  ) {
    return (value as { additionalData?: unknown }).additionalData ?? {};
  }

  return value;
}, storageMetadataSchema);

const bucketSchema = z.object({
  accessPolicy: storageAccessPolicySchema,
  allowedContentTypes: z.array(z.string()).nullish().transform((value) => value ?? []),
  archivedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  description: storageDescriptionSchema,
  displayName: storageDisplayNameSchema,
  id: idSchema,
  key: storageBucketKeySchema,
  maxObjectSizeBytes: byteCountSchema.positive(),
  objectCount: byteCountSchema,
  quotaBytes: byteCountSchema.positive(),
  status: storageResourceStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  usedBytes: byteCountSchema,
  version: positiveVersionSchema,
});

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

const objectSchema = z.object({
  applicationId: optionalIdSchema,
  bucketId: idSchema,
  completedAt: timestampSchema.nullish(),
  contentType: storageContentTypeSchema,
  createdAt: timestampSchema,
  customMetadata: wireMetadataSchema,
  deletedAt: timestampSchema.nullish(),
  environmentId: optionalIdSchema,
  fileName: storageFileNameSchema,
  id: idSchema,
  objectKey: storageObjectKeySchema,
  sha256: z.string().regex(/^[a-f0-9]{64}$/),
  sizeBytes: byteCountSchema.positive(),
  status: storageObjectStatusSchema,
  tenantId: idSchema,
  updatedAt: timestampSchema,
  version: positiveVersionSchema,
});

const transferTicketSchema = z.object({
  expiresAt: timestampSchema,
  method: z.enum(["GET", "PUT"]),
  requiredHeaders: wireMetadataSchema,
  url: z.string().min(1),
});

const uploadSessionSchema = z.object({
  bucketId: idSchema,
  completedAt: timestampSchema.nullish(),
  createdAt: timestampSchema,
  expiresAt: timestampSchema,
  failureReason: z.string().nullish().transform((value) => value ?? ""),
  id: idSchema,
  object: objectSchema,
  status: storageUploadStatusSchema,
  tenantId: idSchema,
  transfer: transferTicketSchema,
  version: positiveVersionSchema,
});

const bucketsPageSchema = z.object({
  buckets: z.array(bucketSchema).nullish().transform((value) => value ?? []),
  nextPageToken: z.string().nullish().transform((value) => value ?? ""),
});
const objectsPageSchema = z.object({
  nextPageToken: z.string().nullish().transform((value) => value ?? ""),
  objects: z.array(objectSchema).nullish().transform((value) => value ?? []),
});
const pageSchema = z.object({
  pageSize: z.number().int().min(1).max(100).default(50),
  pageToken: z.string().max(2_048).default(""),
  query: z.string().trim().max(200).default(""),
});
const bucketInputSchema = z
  .object({
    accessPolicy: storageAccessPolicySchema,
    allowedContentTypes: z.array(z.string().trim().toLowerCase()).max(32),
    description: storageDescriptionSchema,
    displayName: storageDisplayNameSchema,
    maxObjectSizeBytes: z.number().int().positive().safe(),
    quotaBytes: z.number().int().positive().safe(),
  })
  .refine(
    (value) => value.maxObjectSizeBytes <= value.quotaBytes,
    "Maximum object size cannot exceed the bucket quota.",
  );

export type StorageAccessPolicy = z.infer<typeof storageAccessPolicySchema>;
export type StorageBucketRecord = z.infer<typeof bucketSchema>;
export type StorageObjectRecord = z.infer<typeof objectSchema>;
export type StorageTransferTicket = z.infer<typeof transferTicketSchema>;
export type StorageUploadSessionRecord = z.infer<typeof uploadSessionSchema>;
export type StorageMetadata = z.infer<typeof storageMetadataSchema>;

export async function listStorageBuckets(
  tenantId: string,
  options: z.input<typeof pageSchema> & { includeArchived?: boolean },
) {
  const page = pageSchema.parse(options);
  const response = await bucketsBuilder(idSchema.parse(tenantId)).get({
    queryParameters: {
      ...page,
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
    },
  });
  return bucketsPageSchema.parse(requireResponse(response));
}

export async function getStorageBucket(tenantId: string, bucketId: string) {
  const response = await bucketsBuilder(idSchema.parse(tenantId))
    .byBucketId(idSchema.parse(bucketId))
    .get();
  return bucketSchema.parse(requireResponse(response));
}

export async function createStorageBucket(
  csrfToken: string,
  tenantId: string,
  input: z.input<typeof bucketInputSchema> & { key: string },
) {
  const parsed = bucketInputSchema.extend({ key: storageBucketKeySchema }).parse(input);
  const response = await bucketsBuilder(idSchema.parse(tenantId), csrfToken).post(parsed);
  return bucketSchema.parse(requireResponse(response));
}

export async function updateStorageBucket(
  csrfToken: string,
  bucket: StorageBucketRecord,
  input: z.input<typeof bucketInputSchema>,
) {
  const current = bucketSchema.parse(bucket);
  const parsed = bucketInputSchema.parse(input);
  const response = await bucketsBuilder(current.tenantId, csrfToken)
    .byBucketId(current.id)
    .patch({ ...parsed, expectedVersion: current.version });
  return bucketSchema.parse(requireResponse(response));
}

export async function archiveStorageBucket(
  csrfToken: string,
  bucket: StorageBucketRecord,
) {
  const current = bucketSchema.parse(bucket);
  const response = await bucketsBuilder(current.tenantId, csrfToken)
    .byBucketId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return bucketSchema.parse(requireResponse(response));
}

export async function restoreStorageBucket(
  csrfToken: string,
  bucket: StorageBucketRecord,
) {
  const current = bucketSchema.parse(bucket);
  const response = await bucketsBuilder(current.tenantId, csrfToken)
    .withBucketIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return bucketSchema.parse(requireResponse(response));
}

export async function listStorageObjects(
  tenantId: string,
  bucketId: string,
  options: z.input<typeof pageSchema> & { includeDeleted?: boolean },
) {
  const page = pageSchema.parse(options);
  const response = await objectsBuilder(idSchema.parse(tenantId), idSchema.parse(bucketId)).get({
    queryParameters: {
      ...page,
      includeDeleted: z.boolean().default(false).parse(options.includeDeleted),
    },
  });
  return objectsPageSchema.parse(requireResponse(response));
}

export async function getStorageObject(
  tenantId: string,
  bucketId: string,
  objectId: string,
) {
  const response = await objectsBuilder(idSchema.parse(tenantId), idSchema.parse(bucketId))
    .byObjectId(idSchema.parse(objectId))
    .get();
  return objectSchema.parse(requireResponse(response));
}

export async function updateStorageObjectMetadata(
  csrfToken: string,
  storageObject: StorageObjectRecord,
  fileName: string,
  customMetadata: StorageMetadata,
) {
  const current = objectSchema.parse(storageObject);
  const response = await objectsBuilder(current.tenantId, current.bucketId, csrfToken)
    .byObjectId(current.id)
    .metadata.patch({
      customMetadata: { additionalData: storageMetadataSchema.parse(customMetadata) },
      expectedVersion: current.version,
      fileName: storageFileNameSchema.parse(fileName),
    });
  return objectSchema.parse(requireResponse(response));
}

export async function createStorageUploadSession(
  csrfToken: string,
  tenantId: string,
  bucketId: string,
  input: {
    applicationId?: string;
    contentType: string;
    customMetadata: StorageMetadata;
    environmentId?: string;
    fileName: string;
    objectKey: string;
    sha256: string;
    sizeBytes: number;
  },
) {
  const parsed = z
    .object({
      applicationId: idSchema.optional(),
      contentType: storageContentTypeSchema,
      customMetadata: storageMetadataSchema,
      environmentId: idSchema.optional(),
      fileName: storageFileNameSchema,
      objectKey: storageObjectKeySchema,
      sha256: z.string().regex(/^[a-f0-9]{64}$/),
      sizeBytes: z.number().int().positive().safe(),
    })
    .refine((value) => !value.environmentId || Boolean(value.applicationId), {
      message: "Choose an application before choosing an environment.",
    })
    .parse(input);
  const response = await bucketsBuilder(idSchema.parse(tenantId), csrfToken)
    .byBucketId(idSchema.parse(bucketId))
    .uploads.post({
      ...parsed,
      applicationId: parsed.applicationId ?? "",
      customMetadata: { additionalData: parsed.customMetadata },
      environmentId: parsed.environmentId ?? "",
    });
  return uploadSessionSchema.parse(requireResponse(response));
}

export async function completeStorageUpload(
  csrfToken: string,
  session: StorageUploadSessionRecord,
) {
  const current = uploadSessionSchema.parse(session);
  const response = await bucketsBuilder(current.tenantId, csrfToken)
    .byBucketId(current.bucketId)
    .uploads.withUploadSessionIdComplete(current.id)
    .post({ expectedObjectVersion: current.object.version });
  return objectSchema.parse(requireResponse(response));
}

export async function createStorageDownloadUrl(
  csrfToken: string,
  storageObject: StorageObjectRecord,
  lifetimeSeconds = 300,
) {
  const current = objectSchema.parse(storageObject);
  const response = await objectsBuilder(current.tenantId, current.bucketId, csrfToken)
    .withObjectIdDownload(current.id)
    .post({ lifetimeSeconds: z.number().int().min(30).max(3_600).parse(lifetimeSeconds) });
  return transferTicketSchema.parse(requireResponse(response));
}

export async function copyStorageObject(
  csrfToken: string,
  storageObject: StorageObjectRecord,
  input: {
    customMetadata: StorageMetadata;
    fileName: string;
    objectKey: string;
    targetBucketId: string;
  },
) {
  const current = objectSchema.parse(storageObject);
  const parsed = z
    .object({
      customMetadata: storageMetadataSchema,
      fileName: storageFileNameSchema,
      objectKey: storageObjectKeySchema,
      targetBucketId: idSchema,
    })
    .parse(input);
  const response = await objectsBuilder(current.tenantId, current.bucketId, csrfToken)
    .withObjectIdCopy(current.id)
    .post({ ...parsed, customMetadata: { additionalData: parsed.customMetadata } });
  return objectSchema.parse(requireResponse(response));
}

export async function deleteStorageObject(
  csrfToken: string,
  storageObject: StorageObjectRecord,
) {
  const current = objectSchema.parse(storageObject);
  const response = await objectsBuilder(current.tenantId, current.bucketId, csrfToken)
    .byObjectId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return objectSchema.parse(requireResponse(response));
}

export async function uploadStorageTransfer(
  csrfToken: string,
  ticket: StorageTransferTicket,
  file: File,
) {
  const current = transferTicketSchema.parse(ticket);
  if (current.method !== "PUT") throw new Error("The upload ticket does not allow PUT.");
  const target = transferTarget(current.url);
  const headers = new Headers(current.requiredHeaders);
  if (target.viaBff) headers.set("x-csrf-token", csrfToken);
  const response = await fetch(target.url, {
    body: file,
    credentials: target.viaBff ? "same-origin" : "omit",
    headers,
    method: "PUT",
    signal: AbortSignal.timeout(30 * 60_000),
  });
  await requireTransferSuccess(response, "upload");
}

export async function downloadStorageTransfer(
  ticket: StorageTransferTicket,
  expected: Pick<StorageObjectRecord, "sha256" | "sizeBytes">,
) {
  const current = transferTicketSchema.parse(ticket);
  if (current.method !== "GET") throw new Error("The download ticket does not allow GET.");
  const target = transferTarget(current.url);
  const response = await fetch(target.url, {
    credentials: target.viaBff ? "same-origin" : "omit",
    headers: new Headers(current.requiredHeaders),
    method: "GET",
    signal: AbortSignal.timeout(30 * 60_000),
  });
  await requireTransferSuccess(response, "download");
  const bytes = await response.arrayBuffer();
  if (bytes.byteLength !== expected.sizeBytes) {
    throw new Error("The downloaded object size does not match its verified metadata.");
  }
  const digest = await sha256Hex(bytes);
  if (digest !== expected.sha256) {
    throw new Error("The downloaded object failed SHA-256 verification.");
  }
  return new Blob([bytes], { type: response.headers.get("content-type") ?? undefined });
}

export async function sha256Hex(value: ArrayBuffer | Blob): Promise<string> {
  const bytes = value instanceof Blob ? await value.arrayBuffer() : value;
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

export function parseStorageMetadata(value: string): StorageMetadata {
  const entries: Record<string, string> = {};
  for (const [index, rawLine] of value.split(/\r?\n/).entries()) {
    const line = rawLine.trim();
    if (!line) continue;
    const separator = line.indexOf("=");
    if (separator <= 0) {
      throw new Error(`Metadata line ${index + 1} must use key=value.`);
    }
    const key = line.slice(0, separator).trim().toLowerCase();
    const entryValue = line.slice(separator + 1).trim();
    entries[key] = entryValue;
  }
  return storageMetadataSchema.parse(entries);
}

export function formatStorageMetadata(metadata: StorageMetadata): string {
  return Object.entries(storageMetadataSchema.parse(metadata))
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${key}=${value}`)
    .join("\n");
}

export function storageErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted storage values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The storage operation could not be completed.";
}

function bucketsBuilder(tenantId: string, csrfToken?: string) {
  return getAsterloomApiClient(csrfToken).api.v1.tenants.byTenantId(tenantId).storage.buckets;
}

function objectsBuilder(tenantId: string, bucketId: string, csrfToken?: string) {
  return bucketsBuilder(tenantId, csrfToken).byBucketId(bucketId).objects;
}

function transferTarget(value: string) {
  if (value.startsWith("/api/v1/storage/transfers/")) {
    if (typeof window === "undefined") throw new Error("Storage transfers require a browser.");
    return { url: window.location.origin + "/api/asterloom" + value, viaBff: true };
  }
  const url = new URL(value);
  if (url.protocol !== "https:" && url.protocol !== "http:") {
    throw new Error("The storage ticket uses an unsupported URL scheme.");
  }
  return { url: url.toString(), viaBff: false };
}

async function requireTransferSuccess(response: Response, operation: string) {
  if (response.ok) return;
  const message = await response.text().catch(() => "");
  throw new Error(
    message || `The storage ${operation} endpoint returned HTTP ${response.status}.`,
  );
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Storage API returned an empty response.");
  return response;
}
