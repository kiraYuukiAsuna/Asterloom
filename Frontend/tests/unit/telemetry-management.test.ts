import { afterEach, describe, expect, it, vi } from "vitest";

import { TelemetrySignalTypeObject } from "@/lib/api/generated/models";
import {
  listTelemetryRecords,
  type TelemetryScope,
} from "@/lib/api/telemetry-management";

const recordsGet = vi.hoisted(() => vi.fn());

vi.mock("@/lib/api/asterloom-client", () => ({
  getAsterloomApiClient: () => ({
    api: {
      v1: {
        tenants: {
          byTenantId: () => ({
            applications: {
              byApplicationId: () => ({
                environments: {
                  byEnvironmentId: () => ({
                    telemetry: {
                      records: { get: recordsGet },
                    },
                  }),
                },
              }),
            },
          }),
        },
      },
    },
  }),
}));

afterEach(() => {
  recordsGet.mockReset();
});

describe("telemetry management parsing", () => {
  const scope: TelemetryScope = {
    applicationId: "00000000-0000-4000-8000-000000000002",
    environmentId: "00000000-0000-4000-8000-000000000003",
    tenantId: "00000000-0000-4000-8000-000000000001",
  };
  const timestamp = "2026-09-04T00:00:00Z";

  it("accepts hash-derived telemetry record UUIDs without RFC version or variant bits", async () => {
    const hashDerivedId = "11111111-1111-1111-1111-111111111111";
    recordsGet.mockResolvedValue({
      nextPageToken: "",
      records: [
        {
          attributesJson: "{}",
          category: "server",
          createdAt: timestamp,
          durationMilliseconds: null,
          id: hashDerivedId,
          name: "grpc.request",
          observedAt: timestamp,
          payloadJson: "{}",
          scope,
          serviceName: "asterloom.server",
          signalType: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
          spanId: "",
          traceId: "",
          value: "",
        },
      ],
    });

    const page = await listTelemetryRecords(scope, {
      signalType: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
    });

    expect(page.records).toHaveLength(1);
    expect(page.records[0]?.id).toBe(hashDerivedId);
  });

  it("still rejects malformed telemetry record UUIDs", async () => {
    recordsGet.mockResolvedValue({
      nextPageToken: "",
      records: [
        {
          attributesJson: "{}",
          category: "server",
          createdAt: timestamp,
          durationMilliseconds: null,
          id: "not-a-uuid",
          name: "grpc.request",
          observedAt: timestamp,
          payloadJson: "{}",
          scope,
          serviceName: "asterloom.server",
          signalType: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
          spanId: "",
          traceId: "",
          value: "",
        },
      ],
    });

    await expect(
      listTelemetryRecords(scope, {
        signalType: TelemetrySignalTypeObject.TELEMETRY_SIGNAL_TYPE_TRACE,
      }),
    ).rejects.toThrow(/Invalid UUID format/);
  });
});
