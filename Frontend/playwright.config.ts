import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  workers: 4,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: [
    {
      command:
        "dotnet run --project ../Backend/Asterloom.Server/Asterloom.Server.csproj --configuration Debug --urls http://127.0.0.1:5080",
      url: "http://127.0.0.1:5080/health/ready",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        Persistence__Provider: "Memory",
        Identity__Issuer: "http://127.0.0.1:5080/",
        Identity__Bootstrap__AdminDisplayName: "E2E Administrator",
        Identity__Bootstrap__AdminEmail: "admin@asterloom.test",
        Identity__Bootstrap__AdminPassword: "Asterloom-E2E-Admin!2026",
        Identity__RateLimiting__LoginPermitLimit: "100",
        Identity__WebClient__ClientId: "asterloom-web-e2e",
        Identity__WebClient__ClientSecret: "Asterloom-Web-E2E-Secret!2026",
        Identity__WebClient__RedirectUri:
          "http://localhost:3000/api/auth/callback",
        Identity__WebClient__PostLogoutRedirectUri:
          "http://localhost:3000/api/auth/logout/callback",
      },
    },
    {
      command:
        "npm run build && npm run start -- --hostname 127.0.0.1 --port 3000",
      url: "http://127.0.0.1:3000",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: {
        ASTERLOOM_ALLOW_INSECURE_DEVELOPMENT: "true",
        ASTERLOOM_BACKEND_URL: "http://127.0.0.1:5080",
        ASTERLOOM_PASSPORT_PUBLIC_URL: "http://127.0.0.1:5080",
        ASTERLOOM_WEB_ORIGIN: "http://localhost:3000",
        ASTERLOOM_OIDC_ISSUER: "http://127.0.0.1:5080",
        ASTERLOOM_OIDC_CLIENT_ID: "asterloom-web-e2e",
        ASTERLOOM_OIDC_CLIENT_SECRET: "Asterloom-Web-E2E-Secret!2026",
        ASTERLOOM_SESSION_STORE: "memory",
        ASTERLOOM_NEXT_STANDALONE: "false",
        ASTERLOOM_SESSION_ENCRYPTION_KEY:
          "fP0onfOHksI6iuEJADvkZGIWobkecx4whP0vDIp73GE=",
      },
    },
  ],
});
