import { DefaultRequestAdapter } from "@microsoft/kiota-bundle";

import {
  createAsterloomApiClient,
  type AsterloomApiClient,
} from "@/lib/api/generated/asterloomApiClient";

let anonymousClient: AsterloomApiClient | undefined;
let authenticatedClient:
  | { csrfToken: string; client: AsterloomApiClient }
  | undefined;

function createClient(csrfToken?: string): AsterloomApiClient {
  const authenticationProvider: ConstructorParameters<
    typeof DefaultRequestAdapter
  >[0] = {
    authenticateRequest: async (request) => {
      if (csrfToken) {
        request.headers.tryAdd("x-csrf-token", csrfToken);
      }
    },
  };

  const adapter = new DefaultRequestAdapter(authenticationProvider);
  const origin =
    typeof window === "undefined" ? "http://127.0.0.1:3000" : window.location.origin;

  adapter.baseUrl = origin + "/api/asterloom";
  return createAsterloomApiClient(adapter);
}

export function getAsterloomApiClient(csrfToken?: string): AsterloomApiClient {
  if (!csrfToken) {
    anonymousClient ??= createClient();
    return anonymousClient;
  }

  if (authenticatedClient?.csrfToken !== csrfToken) {
    authenticatedClient = { csrfToken, client: createClient(csrfToken) };
  }

  return authenticatedClient.client;
}
