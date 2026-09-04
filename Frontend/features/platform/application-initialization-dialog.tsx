"use client";

import {
  Check,
  CheckCircle2,
  CircleAlert,
  Clipboard,
  LoaderCircle,
  X,
} from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import {
  createAnalyticsSchema,
  createAnalyticsWriteKey,
} from "@/lib/api/analytics-management";
import {
  createPermission,
  createPolicyRule,
  createRole,
  setRoleBinding,
} from "@/lib/api/authorization-management";
import {
  createConfigEntry,
  publishConfigEntry,
  validateConfigDraft,
} from "@/lib/api/config-management";
import {
  createFlag,
  publishFlag,
  validateFlagDraft,
} from "@/lib/api/feature-management";
import {
  createClient,
  createScope,
} from "@/lib/api/identity-management";
import {
  ConfigValueKindObject,
  ConfigVisibilityObject,
  FeatureValueKindObject,
  OidcApplicationTypeObject,
  OidcClientTypeObject,
  OidcGrantTypeObject,
  PolicyEffectObject,
  PolicySubjectTypeObject,
  StorageAccessPolicyObject,
} from "@/lib/api/generated/models";
import {
  createEnvironment,
  type ApplicationRecord,
  type TenantRecord,
} from "@/lib/api/platform-management";
import { createReleaseChannel } from "@/lib/api/release-management";
import { createStorageBucket } from "@/lib/api/storage-management";
import {
  createTelemetrySource,
  getTelemetrySettings,
  updateTelemetrySettings,
} from "@/lib/api/telemetry-management";
import { translate } from "@/lib/i18n/locale";
import { cn } from "@/lib/utils/cn";

type PresetId =
  | "api"
  | "desktop"
  | "server"
  | "authorization"
  | "runtime"
  | "storage"
  | "release"
  | "analytics"
  | "telemetry";
type Phase = "select" | "review" | "running" | "complete" | "error";

const presetIds: PresetId[] = [
  "api",
  "desktop",
  "server",
  "authorization",
  "runtime",
  "storage",
  "release",
  "analytics",
  "telemetry",
];

const presetCopy: Record<
  PresetId,
  { description: string; title: string }
> = {
  api: {
    title: "API scope",
    description: "Create an application API scope and audience.",
  },
  desktop: {
    title: "Interactive OIDC client",
    description: "Public native desktop client with authorization code, PKCE, refresh tokens, and membership auto-join.",
  },
  server: {
    title: "Server OIDC client",
    description: "Confidential client for client credentials and trusted user registration.",
  },
  authorization: {
    title: "Permissions and policies",
    description: "Create application access plus default runtime Allow policies and the server mail role.",
  },
  runtime: {
    title: "Feature and config",
    description: "Publish a default enabled feature flag and empty client JSON configuration.",
  },
  storage: {
    title: "Storage",
    description: "Create a private application files bucket with safe starter limits.",
  },
  release: {
    title: "Release",
    description: "Create the stable release channel. Signing keys and artifacts remain manual.",
  },
  analytics: {
    title: "Analytics",
    description: "Create a starter event schema and one-time environment write key.",
  },
  telemetry: {
    title: "Telemetry",
    description: "Register matching client/server sources and enable database-backed traces, metrics, and logs.",
  },
};

export function ApplicationInitializationDialog({
  application,
  csrfToken,
  onClose,
  onInitialized,
  tenant,
}: {
  application: ApplicationRecord;
  csrfToken: string;
  onClose: () => void;
  onInitialized: () => Promise<unknown>;
  tenant: TenantRecord;
}) {
  const [phase, setPhase] = useState<Phase>("select");
  const [selected, setSelected] = useState<Record<PresetId, boolean>>(
    Object.fromEntries(presetIds.map((id) => [id, true])) as Record<
      PresetId,
      boolean
    >,
  );
  const [completed, setCompleted] = useState<string[]>([]);
  const [currentStep, setCurrentStep] = useState("");
  const [error, setError] = useState("");
  const [serverSecret, setServerSecret] = useState("");
  const [analyticsSecret, setAnalyticsSecret] = useState("");
  const names = initializationNames(tenant, application);

  const selectedCount =
    1 + presetIds.filter((id) => selected[id]).length;

  async function initialize() {
    setPhase("running");
    setCompleted([]);
    setError("");
    let activeStep = "";
    let environmentId = "";
    let serverClientId = "";

    async function runStep(label: string, work: () => Promise<void>) {
      activeStep = label;
      setCurrentStep(label);
      await work();
      setCompleted((current) => [...current, label]);
    }

    try {
      await runStep("Production environment", async () => {
        const environment = await createEnvironment(
          csrfToken,
          tenant.id,
          application.id,
          {
            displayName: "Production",
            environmentType: "ENVIRONMENT_TYPE_PRODUCTION",
            isProtected: true,
            slug: "production",
          },
        );
        environmentId = environment.id;
      });

      if (selected.api) {
        await runStep(presetCopy.api.title, async () => {
          await createScope(csrfToken, {
            description: "API access for " + application.displayName + ".",
            displayName: names.label("API"),
            name: names.apiScope,
            resources: [names.apiAudience],
          });
        });
      }

      if (selected.desktop) {
        await runStep(presetCopy.desktop.title, async () => {
          await createClient(csrfToken, {
            allowMembershipAutoJoin: true,
            allowUserRegistration: false,
            applicationId: application.id,
            applicationType:
              OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_NATIVE,
            clientId: names.desktopClient,
            clientType: OidcClientTypeObject.OIDC_CLIENT_TYPE_PUBLIC,
            displayName: names.label("Desktop"),
            grantTypes: [
              OidcGrantTypeObject.OIDC_GRANT_TYPE_AUTHORIZATION_CODE,
              OidcGrantTypeObject.OIDC_GRANT_TYPE_REFRESH_TOKEN,
            ],
            postLogoutRedirectUris: [
              "http://localhost/signout-callback-oidc",
            ],
            redirectUris: ["http://localhost/"],
            scopes: [
              "openid",
              "profile",
              "email",
              "roles",
              "offline_access",
              "asterloom.api",
              ...(selected.api ? [names.apiScope] : []),
            ],
            tenantId: tenant.id,
          });
        });
      }

      if (selected.server) {
        await runStep(presetCopy.server.title, async () => {
          const credential = await createClient(csrfToken, {
            allowMembershipAutoJoin: false,
            allowUserRegistration: true,
            applicationId: application.id,
            applicationType:
              OidcApplicationTypeObject.OIDC_APPLICATION_TYPE_WEB,
            clientId: names.serverClient,
            clientType: OidcClientTypeObject.OIDC_CLIENT_TYPE_CONFIDENTIAL,
            displayName: names.label("Server"),
            grantTypes: [
              OidcGrantTypeObject.OIDC_GRANT_TYPE_CLIENT_CREDENTIALS,
            ],
            postLogoutRedirectUris: [],
            redirectUris: [],
            scopes: ["asterloom.api"],
            tenantId: tenant.id,
          });
          serverClientId = credential.client.clientId;
          setServerSecret(credential.clientSecret);
        });
      }

      if (selected.authorization) {
        await runStep(presetCopy.authorization.title, async () => {
          await createPermission(csrfToken, {
            description: "Use " + application.displayName + ".",
            displayName: names.label("Access"),
            key: names.accessPermission,
            scope: {
              applicationId: application.id,
              tenantId: tenant.id,
            },
          });

          const policyScope = {
            applicationId: application.id,
            environmentId,
            tenantId: tenant.id,
          };
          const permissions = [
            names.accessPermission,
            "feature.flag.evaluate",
            "config.snapshot.read",
            "release.update.check",
            "storage.object.upload",
            "storage.object.download",
          ];
          await Promise.all(
            permissions.map((permission) =>
              createPolicyRule(csrfToken, {
                condition: null,
                effect: PolicyEffectObject.POLICY_EFFECT_ALLOW,
                name: policyName(permission),
                permission,
                resourceId: "",
                resourceType: "",
                scope: policyScope,
                subject: "*",
                subjectType: PolicySubjectTypeObject.POLICY_SUBJECT_TYPE_ANY,
              }),
            ),
          );

          if (selected.server && serverClientId) {
            const role = await createRole(csrfToken, {
              description: "Allows the business server to send application email.",
              displayName: names.label("Server"),
              key: names.serverRole,
              permissions: ["mail.delivery.send"],
              scope: {
                applicationId: application.id,
                environmentId: undefined,
                tenantId: tenant.id,
              },
            });
            await setRoleBinding(csrfToken, crypto.randomUUID(), {
              actorId: serverClientId,
              expectedVersion: 0,
              roleId: role.id,
              scope: {
                applicationId: application.id,
                environmentId: undefined,
                tenantId: tenant.id,
              },
            });
          }
        });
      }

      const environmentScope = {
        applicationId: application.id,
        environmentId,
        tenantId: tenant.id,
      };

      if (selected.runtime) {
        await runStep(presetCopy.runtime.title, async () => {
          const flag = await createFlag(csrfToken, environmentScope, {
            definition: {
              allocations: [],
              bucketingSalt: names.featureFlag + "-v1",
              defaultVariantKey: "on",
              enabled: true,
              prerequisites: [],
              targetingRules: [],
              variants: [
                {
                  displayName: "On",
                  key: "on",
                  value: { booleanValue: true },
                },
                {
                  displayName: "Off",
                  key: "off",
                  value: { booleanValue: false },
                },
              ],
            },
            description: "Starter application availability flag.",
            displayName: names.label("Enabled"),
            key: names.featureFlag,
            valueKind: FeatureValueKindObject.FEATURE_VALUE_KIND_BOOLEAN,
          });
          await validateFlagDraft(csrfToken, flag);
          await publishFlag(csrfToken, flag);

          const config = await createConfigEntry(csrfToken, environmentScope, {
            definition: {
              defaultValue: { jsonValue: "{}" },
              schemaJson: "",
              targetingRules: [],
            },
            description: "Starter client configuration.",
            displayName: names.label("Settings"),
            key: names.configEntry,
            valueKind: ConfigValueKindObject.CONFIG_VALUE_KIND_JSON,
            visibility: ConfigVisibilityObject.CONFIG_VISIBILITY_CLIENT,
          });
          await validateConfigDraft(csrfToken, config);
          await publishConfigEntry(csrfToken, config);
        });
      }

      if (selected.storage) {
        await runStep(presetCopy.storage.title, async () => {
          await createStorageBucket(csrfToken, tenant.id, {
            accessPolicy:
              StorageAccessPolicyObject.STORAGE_ACCESS_POLICY_PRIVATE,
            allowedContentTypes: [],
            description: "Private files for " + application.displayName + ".",
            displayName: names.label("Files"),
            key: names.storageBucket,
            maxObjectSizeBytes: 100 * 1024 * 1024,
            quotaBytes: 1024 * 1024 * 1024,
          });
        });
      }

      if (selected.release) {
        await runStep(presetCopy.release.title, async () => {
          await createReleaseChannel(csrfToken, environmentScope, {
            description: "Stable releases for " + application.displayName + ".",
            displayName: "Stable",
            key: "stable",
          });
        });
      }

      if (selected.analytics) {
        await runStep(presetCopy.analytics.title, async () => {
          await createAnalyticsSchema(csrfToken, environmentScope, {
            description: "Starter application event.",
            displayName: names.label("Started"),
            key: names.analyticsEvent,
            retentionDays: 30,
            schemaJson: JSON.stringify({
              additionalProperties: true,
              type: "object",
            }),
          });
          const credential = await createAnalyticsWriteKey(
            csrfToken,
            environmentScope,
            names.label("Analytics"),
          );
          setAnalyticsSecret(credential.secret);
        });
      }

      if (selected.telemetry) {
        await runStep(presetCopy.telemetry.title, async () => {
          await Promise.all([
            createTelemetrySource(csrfToken, environmentScope, {
              description: "Interactive desktop client.",
              displayName: names.label("Client"),
              key: names.telemetryClient,
              resourceAttributesJson: "{}",
              serviceName: names.clientService,
            }),
            createTelemetrySource(csrfToken, environmentScope, {
              description: "Business API server.",
              displayName: names.label("Server"),
              key: names.telemetryServer,
              resourceAttributesJson: "{}",
              serviceName: names.serverService,
            }),
          ]);
          const settings = await getTelemetrySettings(environmentScope);
          await updateTelemetrySettings(csrfToken, settings, {
            diagnosticsBaseUrl: settings.diagnosticsBaseUrl,
            exporterEndpoint: settings.exporterEndpoint,
            exporterProtocol: settings.exporterProtocol,
            logsEnabled: true,
            metricsEnabled: true,
            samplingRatio: 1,
            tracesEnabled: true,
          });
        });
      }

      setCurrentStep("");
      setPhase("complete");
      toast.success(translate("Application initialization complete."));
    } catch (cause) {
      setCurrentStep("");
      setError(
        translate("Failed while creating {0}: {1}", {
          0: translate(activeStep),
          1: initializationError(cause),
        }),
      );
      setPhase("error");
    } finally {
      void onInitialized().catch(() => undefined);
    }
  }

  const canClose = phase !== "running";

  return (
    <div
      className="fixed inset-0 z-50 overflow-y-auto bg-slate-950/85 p-4 backdrop-blur-sm"
      data-testid="application-initialization-dialog"
      role="dialog"
      aria-labelledby="application-initialization-title"
      aria-modal="true"
    >
      <div className="mx-auto my-6 max-w-3xl rounded-2xl border border-white/10 bg-slate-950/95 p-5 shadow-2xl sm:p-7">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-sky-400">
              {translate("New application")}
            </p>
            <h2
              className="mt-2 text-xl font-semibold text-white"
              id="application-initialization-title"
            >
              {translate("Initialize " + application.displayName)}
            </h2>
            <p className="mt-2 text-sm leading-6 text-slate-400">
              {translate("Select the production-ready resources to create now. You can still configure every resource later.")}
            </p>
          </div>
          <Button
            aria-label={translate("Close initialization")}
            disabled={!canClose}
            onClick={onClose}
            size="icon"
            type="button"
            variant="ghost"
          >
            <X aria-hidden="true" className="size-4" />
          </Button>
        </div>

        {(phase === "select" || phase === "review") && (
          <>
            <div className="mt-6 rounded-xl border border-emerald-400/20 bg-emerald-400/[0.06] p-4">
              <label className="flex items-start gap-3">
                <input
                  checked
                  className="mt-1 size-4 accent-emerald-400"
                  disabled
                  readOnly
                  type="checkbox"
                />
                <span>
                  <span className="block text-sm font-semibold text-emerald-200">
                    {translate("Production environment")}
                  </span>
                  <span className="mt-1 block text-xs leading-5 text-slate-300">
                    {translate("Required: protected Production environment with slug production.")}
                  </span>
                </span>
              </label>
            </div>

            {phase === "select" ? (
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                {presetIds.map((id) => (
                  <label
                    className={cn(
                      "cursor-pointer rounded-xl border p-4 transition",
                      selected[id]
                        ? "border-sky-400/30 bg-sky-400/[0.06]"
                        : "border-white/8 bg-white/[0.02]",
                    )}
                    key={id}
                  >
                    <span className="flex items-start gap-3">
                      <input
                        autoFocus={id === "api"}
                        checked={selected[id]}
                        className="mt-1 size-4 accent-sky-400"
                        onChange={(event) =>
                          setSelected((current) => ({
                            ...current,
                            [id]: event.target.checked,
                          }))
                        }
                        type="checkbox"
                      />
                      <span>
                        <span className="block text-sm font-semibold text-slate-200">
                          {translate(presetCopy[id].title)}
                        </span>
                        <span className="mt-1 block text-xs leading-5 text-slate-400">
                          {translate(presetCopy[id].description)}
                        </span>
                      </span>
                    </span>
                  </label>
                ))}
              </div>
            ) : (
              <Review
                names={names}
                selected={selected}
                selectedCount={selectedCount}
              />
            )}

            <div className="mt-6 flex flex-wrap justify-between gap-3 border-t border-white/8 pt-5">
              <Button
                data-ui-action="application-initialization-skip"
                onClick={onClose}
                type="button"
                variant="ghost"
              >
                {translate("Skip initialization")}
              </Button>
              <div className="flex gap-2">
                {phase === "review" && (
                  <Button
                    onClick={() => setPhase("select")}
                    type="button"
                    variant="outline"
                  >
                    {translate("Back")}
                  </Button>
                )}
                {phase === "select" ? (
                  <Button
                    data-ui-action="application-initialization-next"
                    onClick={() => setPhase("review")}
                    type="button"
                  >
                    {translate("Review {0} initialization steps", {
                      0: selectedCount,
                    })}
                  </Button>
                ) : (
                  <Button
                    data-ui-action="application-initialization-run"
                    onClick={() => void initialize()}
                    type="button"
                  >
                    {translate("Initialize application")}
                  </Button>
                )}
              </div>
            </div>
          </>
        )}

        {(phase === "running" ||
          phase === "complete" ||
          phase === "error") && (
          <InitializationResult
            analyticsSecret={analyticsSecret}
            completed={completed}
            currentStep={currentStep}
            error={error}
            names={names}
            onClose={onClose}
            phase={phase}
            serverSecret={serverSecret}
          />
        )}
      </div>
    </div>
  );
}

function Review({
  names,
  selected,
  selectedCount,
}: {
  names: InitializationNames;
  selected: Record<PresetId, boolean>;
  selectedCount: number;
}) {
  const rows = [
    ["Environment", "Production (production)", true],
    ["API scope", names.apiScope + " → " + names.apiAudience, selected.api],
    ["Desktop client", names.desktopClient, selected.desktop],
    ["Server client", names.serverClient, selected.server],
    [
      "Permission",
      names.accessPermission +
        " + 6 Allow policies" +
        (selected.server ? " + " + names.serverRole + " mail role" : ""),
      selected.authorization,
    ],
    ["Feature / Config", names.featureFlag + " / " + names.configEntry, selected.runtime],
    ["Storage bucket", names.storageBucket, selected.storage],
    ["Release channel", "stable", selected.release],
    ["Analytics event", names.analyticsEvent, selected.analytics],
    ["Telemetry services", names.clientService + " / " + names.serverService, selected.telemetry],
  ] as const;

  return (
    <div className="mt-5">
      <p className="text-sm text-slate-300">
        {translate("{0} initialization steps will run.", {
          0: selectedCount,
        })}
      </p>
      <dl className="mt-3 divide-y divide-white/8 rounded-xl border border-white/8 bg-slate-950/45 px-4">
        {rows
          .filter((row) => row[2])
          .map(([label, value]) => (
            <div className="grid gap-1 py-3 sm:grid-cols-[9rem_1fr]" key={label}>
              <dt className="text-xs font-medium text-slate-500">
                {translate(label)}
              </dt>
              <dd className="break-all font-mono text-xs text-slate-300">
                {value}
              </dd>
            </div>
          ))}
      </dl>
      <div className="mt-4 rounded-xl border border-amber-400/15 bg-amber-400/[0.05] p-4 text-xs leading-5 text-amber-100/75">
        {translate("Desktop users join the application on first sign-in. Registration is authorized through the trusted server client; no business event is sent automatically. SMTP credentials, release signing keys, and release artifacts require manual configuration.")}
      </div>
    </div>
  );
}

function InitializationResult({
  analyticsSecret,
  completed,
  currentStep,
  error,
  names,
  onClose,
  phase,
  serverSecret,
}: {
  analyticsSecret: string;
  completed: string[];
  currentStep: string;
  error: string;
  names: InitializationNames;
  onClose: () => void;
  phase: Phase;
  serverSecret: string;
}) {
  const finished = phase === "complete";

  return (
    <div className="mt-7">
      <div className="flex items-center gap-3">
        {phase === "running" ? (
          <LoaderCircle
            aria-hidden="true"
            className="size-6 animate-spin text-sky-400"
          />
        ) : finished ? (
          <CheckCircle2 aria-hidden="true" className="size-6 text-emerald-400" />
        ) : (
          <CircleAlert aria-hidden="true" className="size-6 text-rose-400" />
        )}
        <div>
          <h3 className="font-semibold text-white">
            {translate(
              phase === "running"
                ? "Initializing application…"
                : finished
                  ? "Initialization complete"
                  : "Initialization stopped",
            )}
          </h3>
          <p className="mt-1 text-xs text-slate-400" aria-live="polite">
            {currentStep
              ? translate("Creating " + currentStep + "…")
              : translate(completed.length + " steps completed.")}
          </p>
        </div>
      </div>

      {completed.length > 0 && (
        <ul className="mt-5 grid gap-2 sm:grid-cols-2">
          {completed.map((step) => (
            <li
              className="flex items-center gap-2 rounded-lg bg-white/[0.035] px-3 py-2 text-xs text-slate-300"
              key={step}
            >
              <Check aria-hidden="true" className="size-3.5 text-emerald-400" />
              {translate(step)}
            </li>
          ))}
        </ul>
      )}

      {phase === "error" && (
        <div className="mt-5 rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4">
          <p className="text-sm font-medium text-rose-200">{error}</p>
          <p className="mt-2 text-xs leading-5 text-slate-300">
            {translate("Completed resources were kept. Close this window and finish the remaining resources manually to avoid creating duplicates.")}
          </p>
        </div>
      )}

      {(serverSecret || analyticsSecret) && (
        <div className="mt-5 space-y-3 rounded-xl border border-amber-400/20 bg-amber-400/[0.05] p-4">
          <p className="text-sm font-semibold text-amber-100">
            {translate("Copy these credentials now")}
          </p>
          <p className="text-xs text-slate-300">
            {translate("Secrets are shown once and disappear when this window closes.")}
          </p>
          {serverSecret && (
            <Secret
              label={translate("Server client secret")}
              testId="initialization-server-secret"
              value={serverSecret}
            />
          )}
          {analyticsSecret && (
            <Secret
              label={translate("Analytics write key")}
              testId="initialization-analytics-secret"
              value={analyticsSecret}
            />
          )}
          <p className="break-all font-mono text-[10px] text-slate-400">
            {translate("Server client ID")}: {names.serverClient}
          </p>
        </div>
      )}

      {phase !== "running" && (
        <div className="mt-6 flex justify-end border-t border-white/8 pt-5">
          <Button onClick={onClose} type="button">
            {translate("Close")}
          </Button>
        </div>
      )}
    </div>
  );
}

function Secret({
  label,
  testId,
  value,
}: {
  label: string;
  testId: string;
  value: string;
}) {
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(translate("Copied."));
    } catch {
      toast.error(translate("Copy failed."));
    }
  }

  return (
    <div>
      <p className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
        {label}
      </p>
      <div className="flex gap-2">
        <code
          className="min-w-0 flex-1 break-all rounded-lg bg-slate-950 px-3 py-2 text-xs text-amber-100"
          data-testid={testId}
        >
          {value}
        </code>
        <Button
          aria-label={translate("Copy " + label)}
          onClick={() => void copy()}
          size="icon"
          type="button"
          variant="outline"
        >
          <Clipboard aria-hidden="true" className="size-4" />
        </Button>
      </div>
    </div>
  );
}

type InitializationNames = ReturnType<typeof initializationNames>;

function initializationNames(
  tenant: TenantRecord,
  application: ApplicationRecord,
) {
  const base = tenant.slug + "-" + application.slug;
  const dotBase = (tenant.slug + "." + application.slug).replaceAll("-", ".");

  return {
    accessPermission: withSuffix("app." + base, ".access", 200),
    analyticsEvent: withSuffix(base, ".started"),
    apiAudience: withSuffix(base, "-api", 200),
    apiScope: withSuffix(base, ".api"),
    clientService: withSuffix(dotBase, ".client", 200),
    configEntry: withSuffix(base, ".settings"),
    desktopClient: withSuffix(base, "-desktop"),
    featureFlag: withSuffix(base, ".enabled"),
    label: (suffix: string) =>
      (application.displayName.slice(0, 199 - suffix.length).trim() +
        " " +
        suffix).trim(),
    serverClient: withSuffix(base, "-server"),
    serverRole: withSuffix(application.slug, "-server", 64),
    serverService: withSuffix(dotBase, ".server", 200),
    storageBucket: withSuffix(base, "-files"),
    telemetryClient: withSuffix(base, "-client"),
    telemetryServer: withSuffix(base, "-server"),
  };
}

function withSuffix(value: string, suffix: string, maxLength = 100) {
  const normalized = value
    .toLowerCase()
    .replace(/[^a-z0-9._-]/g, "-");
  const safeValue = /^[a-z]/.test(normalized)
    ? normalized
    : "app-" + normalized;
  const prefix = safeValue
    .slice(0, maxLength - suffix.length)
    .replace(/[._-]+$/, "");
  return prefix + suffix;
}

function policyName(permission: string) {
  return ("Allow " + permission).slice(0, 200);
}

function initializationError(error: unknown) {
  if (error instanceof Error) return error.message;
  if (error && typeof error === "object") {
    const candidate = error as { message?: unknown; messageEscaped?: unknown };
    if (typeof candidate.messageEscaped === "string") {
      return candidate.messageEscaped;
    }
    if (typeof candidate.message === "string") return candidate.message;
  }
  return translate("Application initialization failed.");
}
