import { z } from "zod";

import {
  MailAccountStatusObject,
  MailDeliveryStatusObject,
  SmtpSecurityObject,
} from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const idSchema = z.string().uuid();
const timestampSchema = z.string().min(1);
const optionalTextSchema = z.string().nullish().transform((value) => value ?? "");
const optionalTimestampSchema = timestampSchema.nullish().transform((value) => value ?? null);
const emailSchema = z.string().trim().email().max(320);

export const mailScopeSchema = z.object({
  applicationId: idSchema,
  tenantId: idSchema,
});
export const smtpSecuritySchema = z.enum([
  SmtpSecurityObject.SMTP_SECURITY_START_TLS,
  SmtpSecurityObject.SMTP_SECURITY_SSL_ON_CONNECT,
]);
const accountStatusSchema = z.enum([
  MailAccountStatusObject.MAIL_ACCOUNT_STATUS_ACTIVE,
  MailAccountStatusObject.MAIL_ACCOUNT_STATUS_ARCHIVED,
]);
export const deliveryStatusSchema = z.enum([
  MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_PENDING,
  MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_SENT,
  MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_FAILED,
]);
export const smtpAccountInputSchema = z.object({
  fromAddress: emailSchema,
  fromName: z.string().trim().max(200),
  host: z.string().trim().min(1).max(255),
  name: z.string().trim().min(1).max(200),
  port: z.number().int().min(1).max(65_535),
  security: smtpSecuritySchema,
  smtpPassword: z.string().min(1).max(1_024),
  username: z.string().trim().min(1).max(320),
});
export const smtpAccountUpdateSchema = smtpAccountInputSchema.extend({
  smtpPassword: z.string().max(1_024),
});
export const mailMessageInputSchema = z.object({
  bcc: z.array(emailSchema).max(100),
  cc: z.array(emailSchema).max(100),
  clientMessageId: z.string().trim().regex(/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/),
  htmlBody: z.string().max(1_048_576),
  replyTo: z.union([z.literal(""), emailSchema]),
  smtpAccountId: idSchema,
  subject: z.string().trim().min(1).max(200),
  textBody: z.string().max(1_048_576),
  to: z.array(emailSchema).min(1).max(100),
}).refine((value) => value.textBody.length > 0 || value.htmlBody.length > 0, {
  message: "Enter a text or HTML body.",
  path: ["textBody"],
}).refine((value) => value.to.length + value.cc.length + value.bcc.length <= 100, {
  message: "A message cannot contain more than 100 recipients.",
  path: ["to"],
});

const scopeResponseSchema = mailScopeSchema;
const accountSchema = z.object({
  archivedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  fromAddress: emailSchema,
  fromName: optionalTextSchema,
  host: z.string(),
  id: idSchema,
  name: z.string(),
  port: z.number().int().min(1).max(65_535),
  scope: scopeResponseSchema,
  security: smtpSecuritySchema,
  status: accountStatusSchema,
  updatedAt: timestampSchema,
  username: z.string(),
  version: z.number().int().positive().safe(),
});
const accountPageSchema = z.object({
  accounts: z.array(accountSchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});
const deliverySchema = z.object({
  bcc: z.array(emailSchema).nullish().transform((value) => value ?? []),
  cc: z.array(emailSchema).nullish().transform((value) => value ?? []),
  clientMessageId: z.string(),
  completedAt: optionalTimestampSchema,
  createdAt: timestampSchema,
  errorCode: optionalTextSchema,
  errorMessage: optionalTextSchema,
  id: idSchema,
  providerMessageId: optionalTextSchema,
  replyTo: optionalTextSchema,
  scope: scopeResponseSchema,
  smtpAccountId: idSchema,
  status: deliveryStatusSchema,
  subject: z.string(),
  to: z.array(emailSchema).nullish().transform((value) => value ?? []),
});
const deliveryPageSchema = z.object({
  deliveries: z.array(deliverySchema).nullish().transform((value) => value ?? []),
  nextPageToken: optionalTextSchema,
});

export type MailScope = z.infer<typeof mailScopeSchema>;
export type SmtpAccountRecord = z.infer<typeof accountSchema>;
export type MailDeliveryRecord = z.infer<typeof deliverySchema>;
export type SmtpSecurity = z.infer<typeof smtpSecuritySchema>;
export type MailDeliveryStatus = z.infer<typeof deliveryStatusSchema>;

export async function listSmtpAccounts(
  scope: MailScope,
  options: { includeArchived?: boolean; pageSize?: number; pageToken?: string; query?: string },
) {
  const response = await mailBuilder(scope).accounts.get({
    queryParameters: {
      includeArchived: z.boolean().default(false).parse(options.includeArchived),
      pageSize: z.number().int().min(1).max(100).default(25).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      query: z.string().trim().max(200).default("").parse(options.query),
    },
  });
  return accountPageSchema.parse(requireResponse(response));
}

export async function getSmtpAccount(scope: MailScope, accountId: string) {
  const response = await mailBuilder(scope).accounts.bySmtpAccountId(idSchema.parse(accountId)).get();
  return accountSchema.parse(requireResponse(response));
}

export async function createSmtpAccount(
  csrfToken: string,
  scope: MailScope,
  input: z.input<typeof smtpAccountInputSchema>,
) {
  const response = await mailBuilder(scope, csrfToken).accounts.post(smtpAccountInputSchema.parse(input));
  return accountSchema.parse(requireResponse(response));
}

export async function updateSmtpAccount(
  csrfToken: string,
  account: SmtpAccountRecord,
  input: z.input<typeof smtpAccountUpdateSchema>,
) {
  const current = accountSchema.parse(account);
  const response = await mailBuilder(current.scope, csrfToken).accounts
    .bySmtpAccountId(current.id)
    .patch({ ...smtpAccountUpdateSchema.parse(input), expectedVersion: current.version });
  return accountSchema.parse(requireResponse(response));
}

export async function archiveSmtpAccount(csrfToken: string, account: SmtpAccountRecord) {
  const current = accountSchema.parse(account);
  const response = await mailBuilder(current.scope, csrfToken).accounts
    .bySmtpAccountId(current.id)
    .delete({ queryParameters: { expectedVersion: current.version } });
  return accountSchema.parse(requireResponse(response));
}

export async function restoreSmtpAccount(csrfToken: string, account: SmtpAccountRecord) {
  const current = accountSchema.parse(account);
  const response = await mailBuilder(current.scope, csrfToken).accounts
    .withSmtpAccountIdRestore(current.id)
    .post({ expectedVersion: current.version });
  return accountSchema.parse(requireResponse(response));
}

export async function testSmtpAccount(
  csrfToken: string,
  account: SmtpAccountRecord,
  recipient: string,
) {
  const current = accountSchema.parse(account);
  const response = await mailBuilder(current.scope, csrfToken).accounts
    .withSmtpAccountIdTest(current.id)
    .post({ recipient: emailSchema.parse(recipient) });
  return deliverySchema.parse(requireResponse(response));
}

export async function listMailDeliveries(
  scope: MailScope,
  options: { pageSize?: number; pageToken?: string; status?: MailDeliveryStatus },
) {
  const response = await mailBuilder(scope).deliveries.get({
    queryParameters: {
      pageSize: z.number().int().min(1).max(100).default(25).parse(options.pageSize),
      pageToken: z.string().max(2_048).default("").parse(options.pageToken),
      status: options.status,
    },
  });
  return deliveryPageSchema.parse(requireResponse(response));
}

export async function getMailDelivery(scope: MailScope, deliveryId: string) {
  const response = await mailBuilder(scope).deliveries.byDeliveryId(idSchema.parse(deliveryId)).get();
  return deliverySchema.parse(requireResponse(response));
}

export async function sendMail(
  csrfToken: string,
  scope: MailScope,
  input: z.input<typeof mailMessageInputSchema>,
) {
  const parsed = mailMessageInputSchema.parse(input);
  const current = mailScopeSchema.parse(scope);
  const response = await applicationBuilder(current, csrfToken).mailSend.post({
    ...parsed,
    applicationId: current.applicationId,
    tenantId: current.tenantId,
  });
  return deliverySchema.parse(requireResponse(response));
}

export function parseMailAddresses(value: string): string[] {
  if (value.trim().length === 0) return [];
  return value.split(/[;,\n]/u).map((item) => item.trim()).filter(Boolean).map((item) => emailSchema.parse(item));
}

export function mailErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) {
    return error.issues[0]?.message ?? "The submitted mail values are invalid.";
  }
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The mail operation could not be completed.";
}

function applicationBuilder(scope: MailScope, csrfToken?: string) {
  const parsed = mailScopeSchema.parse(scope);
  return getAsterloomApiClient(csrfToken).api.v1.tenants
    .byTenantId(parsed.tenantId)
    .applications.byApplicationId(parsed.applicationId);
}

function mailBuilder(scope: MailScope, csrfToken?: string) {
  return applicationBuilder(scope, csrfToken).mail;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Mail API returned an empty response.");
  return response;
}
