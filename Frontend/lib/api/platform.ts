import type { GetPlatformInfoResponse } from "@/lib/api/generated/models";

import { getAsterloomApiClient } from "./asterloom-client";

export async function getPlatformInfo(): Promise<GetPlatformInfoResponse> {
  const response = await getAsterloomApiClient().api.v1.platform.info.get();

  if (!response) {
    throw new Error("The platform API returned an empty response.");
  }

  return response;
}
