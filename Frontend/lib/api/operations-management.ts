import { z } from "zod";

import { DependencyHealthStatusObject } from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

const optionalTextSchema = z.string().nullish().transform((value) => value ?? "");
const statusSchema = z.enum([
  DependencyHealthStatusObject.DEPENDENCY_HEALTH_STATUS_HEALTHY,
  DependencyHealthStatusObject.DEPENDENCY_HEALTH_STATUS_DEGRADED,
  DependencyHealthStatusObject.DEPENDENCY_HEALTH_STATUS_UNHEALTHY,
]);
const apiEndpointSchema = z.object({
  category: z.enum(["admin", "runtime"]),
  deprecated: z.boolean(),
  httpMethod: z.string().min(1),
  httpPath: z.string().startsWith("/"),
  requestType: z.string().min(1),
  responseType: z.string().min(1),
  rpc: z.string().min(1),
  service: z.string().startsWith("asterloom."),
});
const apiListSchema = z.object({
  apis: z.array(apiEndpointSchema).nullish().transform((value) => value ?? []),
});
const dependencySchema = z.object({
  description: optionalTextSchema,
  durationMilliseconds: z.number().int().nonnegative().safe(),
  name: z.string().min(1),
  status: statusSchema,
  tags: z.array(z.string()).nullish().transform((value) => value ?? []),
});
const healthSchema = z.object({
  checkedAt: z.string().min(1),
  dependencies: z.array(dependencySchema).nullish().transform((value) => value ?? []),
  durationMilliseconds: z.number().int().nonnegative().safe(),
  status: statusSchema,
});
const openApiDocumentSchema = z.object({
  content: z.string().min(2),
  contentType: z.string().min(1),
  generatedAt: z.string().min(1),
  sha256: z.string().regex(/^[0-9a-f]{64}$/),
});

export type OperationsApiEndpoint = z.infer<typeof apiEndpointSchema>;
export type OperationsHealthRecord = z.infer<typeof healthSchema>;
export type OperationsOpenApiDocument = z.infer<typeof openApiDocumentSchema>;

export async function listOperationsApis(options: { category?: "" | "admin" | "runtime"; query?: string }) {
  const response = await operationsBuilder().apis.get({
    queryParameters: {
      category: z.enum(["", "admin", "runtime"]).default("").parse(options.category),
      query: z.string().trim().max(200).default("").parse(options.query),
    },
  });
  return apiListSchema.parse(requireResponse(response));
}

export async function getOperationsHealth() {
  return healthSchema.parse(requireResponse(await operationsBuilder().health.get()));
}

export async function getOperationsOpenApiDocument() {
  return openApiDocumentSchema.parse(requireResponse(await operationsBuilder().openapi.get()));
}

export function operationsErrorMessage(error: unknown): string {
  if (error instanceof z.ZodError) return error.issues[0]?.message ?? "The Operations response is invalid.";
  if (typeof error === "object" && error !== null) {
    const candidate = error as Record<string, unknown>;
    if (typeof candidate.messageEscaped === "string") return candidate.messageEscaped;
    if (typeof candidate.message === "string") return candidate.message;
  }
  return "The Operations request could not be completed.";
}

function operationsBuilder() {
  return getAsterloomApiClient().api.v1.operations;
}

function requireResponse<T>(response: T | undefined): T {
  if (response === undefined) throw new Error("The Operations API returned an empty response.");
  return response;
}
