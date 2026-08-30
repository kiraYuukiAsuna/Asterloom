import { z } from "zod";

import { AuditOutcomeObject } from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const auditOutcomeSchema = z.enum([
  AuditOutcomeObject.AUDIT_OUTCOME_SUCCEEDED,
  AuditOutcomeObject.AUDIT_OUTCOME_DENIED,
  AuditOutcomeObject.AUDIT_OUTCOME_FAILED,
]);
const optionalScopeIdSchema = z
  .union([z.literal(""), z.string().uuid()])
  .nullish()
  .transform((value) => value || undefined);
const auditEventSchema = z.object({
  actorId: z.string().min(1),
  applicationId: optionalScopeIdSchema,
  changeSummary: z.string(),
  createdAt: timestampSchema,
  environmentId: optionalScopeIdSchema,
  errorCode: z.string(),
  id: idSchema,
  operation: z.string().min(1),
  outcome: auditOutcomeSchema,
  requestId: z.string().min(1),
  resourceId: z.string(),
  resourceType: z.string().min(1),
  tenantId: optionalScopeIdSchema,
});
const auditPageSchema = z.object({
  auditEvents: z.array(auditEventSchema),
  nextPageToken: z.string().nullish(),
});
const exportResponseSchema = z.object({
  content: z.string().min(1),
  contentType: z.string().min(1),
  exportedRows: z.number().int().nonnegative(),
  fileName: z.string().min(1),
});
const filterSchema = z.object({
  actorId: z.string().trim().max(200).default(""),
  fromAt: z.string().default(""),
  operation: z.string().trim().max(300).default(""),
  outcome: z.union([z.literal(""), auditOutcomeSchema]).default(""),
  requestId: z.string().trim().max(200).default(""),
  toAt: z.string().default(""),
});

export type AuditEventRecord = z.infer<typeof auditEventSchema>;
export type AuditOutcome = z.infer<typeof auditOutcomeSchema>;
export type AuditFilters = z.infer<typeof filterSchema>;

export async function listAuditEvents(
  options: Partial<AuditFilters> & { pageSize?: number; pageToken?: string },
) {
  const parsed = filterSchema
    .extend({
      pageSize: z.number().int().min(1).max(100).default(50),
      pageToken: z.string().default(""),
    })
    .parse(options);
  const response = await getAsterloomApiClient().api.v1.audit.events.get({
    queryParameters: {
      ...parsed,
      fromAt: parsed.fromAt || undefined,
      outcome: parsed.outcome || undefined,
      toAt: parsed.toAt || undefined,
    },
  });
  return auditPageSchema.parse(requireResponse(response));
}

export async function getAuditEvent(auditEventId: string) {
  const response = await getAsterloomApiClient().api.v1.audit.events
    .byAuditEventId(idSchema.parse(auditEventId))
    .get();
  return auditEventSchema.parse(requireResponse(response));
}

export async function exportAuditEvents(
  csrfToken: string,
  options: Partial<AuditFilters> & { maximumRows?: number },
) {
  const parsed = filterSchema
    .extend({ maximumRows: z.number().int().min(1).max(10_000).default(10_000) })
    .parse(options);
  const response = await getAsterloomApiClient(csrfToken).api.v1.audit.eventsExport.post({
    ...parsed,
    fromAt: parsed.fromAt || undefined,
    outcome: parsed.outcome || undefined,
    toAt: parsed.toAt || undefined,
  });
  return exportResponseSchema.parse(requireResponse(response));
}

export function auditErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    const issue = error.issues[0];
    if (!issue) return "The audit request is invalid.";
    const path = issue.path.length > 0 ? `${issue.path.join(".")}: ` : "";
    return path + issue.message;
  }

  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") {
      return candidate.messageEscaped;
    }
    if (typeof candidate.message === "string") {
      return candidate.message;
    }
  }

  return "The audit operation could not be completed.";
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) {
    throw new Error("The audit API returned an empty response.");
  }
  return response;
}
