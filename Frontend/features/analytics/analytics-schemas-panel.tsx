"use client";

import {
  Archive,
  Clipboard,
  Eye,
  KeyRound,
  LoaderCircle,
  Plus,
  RefreshCw,
  RotateCcw,
  Save,
  ShieldOff,
} from "lucide-react";
import { type FormEvent, useState } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { AnalyticsResourceStatusObject, AnalyticsWriteKeyStatusObject } from "@/lib/api/generated/models";
import {
  analyticsErrorMessage,
  archiveAnalyticsSchema,
  createAnalyticsSchema,
  createAnalyticsWriteKey,
  getAnalyticsSchema,
  listAnalyticsSchemas,
  listAnalyticsWriteKeys,
  restoreAnalyticsSchema,
  revokeAnalyticsWriteKey,
  rotateAnalyticsWriteKey,
  updateAnalyticsRetention,
  updateAnalyticsSchema,
  type AnalyticsEventSchemaRecord,
  type AnalyticsScope,
  type AnalyticsWriteKeyCredential,
  type AnalyticsWriteKeyRecord,
} from "@/lib/api/analytics-management";
import { cn } from "@/lib/utils/cn";

import {
  AnalyticsEmpty,
  AnalyticsError,
  AnalyticsLoading,
  AnalyticsStatusBadge,
  analyticsInputClassName,
  analyticsLabelClassName,
  analyticsTextAreaClassName,
} from "./analytics-ui";

const defaultSchema = JSON.stringify(
  {
    type: "object",
    additionalProperties: false,
    required: ["itemId"],
    properties: {
      itemId: { type: "string" },
    },
  },
  null,
  2,
);

export function AnalyticsSchemasPanel({
  csrfToken,
  scope,
}: {
  csrfToken: string;
  scope: AnalyticsScope;
}) {
  const [includeArchived, setIncludeArchived] = useState(false);
  const [selected, setSelected] = useState<AnalyticsEventSchemaRecord | null>(null);
  const [gettingId, setGettingId] = useState("");
  const schemas = useSWR(
    ["analytics-schemas", scope.tenantId, scope.applicationId, scope.environmentId, includeArchived],
    () => listAnalyticsSchemas(scope, { includeArchived, pageSize: 100 }),
    { keepPreviousData: true },
  );
  const writeKeys = useSWR(
    ["analytics-write-keys", scope.tenantId, scope.applicationId, scope.environmentId],
    () => listAnalyticsWriteKeys(scope, true),
  );

  async function inspect(schema: AnalyticsEventSchemaRecord) {
    setGettingId(schema.id);
    try {
      setSelected(await getAnalyticsSchema(scope, schema.id));
      toast.success("Event schema refreshed.");
    } catch (error) {
      toast.error(analyticsErrorMessage(error));
    } finally {
      setGettingId("");
    }
  }

  async function schemaChanged(schema: AnalyticsEventSchemaRecord) {
    setSelected(schema);
    await schemas.mutate();
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.2fr)_minmax(22rem,0.8fr)]">
        <div className="space-y-6">
          <Card data-ui-action="list-analytics-schemas">
            <CardHeader className="sm:flex-row sm:items-center sm:justify-between">
              <div>
                <CardTitle>Event contracts</CardTitle>
                <CardDescription>
                  JSON Schema controls accepted fields, types, required values, and redaction.
                </CardDescription>
              </div>
              <label className="flex items-center gap-2 text-xs text-slate-400">
                <input
                  aria-label="Include archived analytics schemas"
                  checked={includeArchived}
                  onChange={(event) => setIncludeArchived(event.target.checked)}
                  type="checkbox"
                />
                Include archived
              </label>
            </CardHeader>
            <CardContent>
              {schemas.isLoading ? (
                <AnalyticsLoading label="Loading event schemas" />
              ) : schemas.error ? (
                <AnalyticsError error={schemas.error} />
              ) : (schemas.data?.eventSchemas.length ?? 0) === 0 ? (
                <AnalyticsEmpty message="No event schemas exist in this environment." />
              ) : (
                <div className="space-y-2">
                  {schemas.data?.eventSchemas.map((schema) => (
                    <article
                      className={cn(
                        "flex flex-col gap-3 rounded-xl border p-4 sm:flex-row sm:items-center sm:justify-between",
                        selected?.id === schema.id
                          ? "border-cyan-400/30 bg-cyan-400/[0.06]"
                          : "border-white/8 bg-white/[0.02]",
                      )}
                      data-testid={`analytics-schema-${schema.key}`}
                      key={schema.id}
                    >
                      <button className="min-w-0 text-left" onClick={() => setSelected(schema)} type="button">
                        <span className="flex items-center gap-2">
                          <span className="truncate text-sm font-medium text-white">{schema.displayName}</span>
                          <AnalyticsStatusBadge status={schema.status} />
                        </span>
                        <span className="mt-1 block font-mono text-xs text-cyan-300">{schema.key}</span>
                        <span className="mt-1 block text-xs text-slate-500">
                          {schema.retentionDays} day retention · version {schema.version}
                        </span>
                      </button>
                      <Button
                        data-ui-action="get-analytics-schema"
                        disabled={gettingId === schema.id}
                        onClick={() => void inspect(schema)}
                        size="sm"
                        type="button"
                        variant="outline"
                      >
                        {gettingId === schema.id ? <LoaderCircle className="size-3.5 animate-spin" /> : <Eye className="size-3.5" />}
                        Inspect
                      </Button>
                    </article>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
          <CreateSchemaCard csrfToken={csrfToken} onCreated={schemaChanged} scope={scope} />
        </div>

        {selected ? (
          <SchemaInspector
            csrfToken={csrfToken}
            key={`${selected.id}:${selected.version}`}
            onChanged={schemaChanged}
            schema={selected}
          />
        ) : (
          <AnalyticsEmpty message="Select a schema to edit its contract, lifecycle, or retention." />
        )}
      </div>

      <WriteKeysCard
        csrfToken={csrfToken}
        error={writeKeys.error}
        isLoading={writeKeys.isLoading}
        onChanged={() => writeKeys.mutate()}
        scope={scope}
        writeKeys={writeKeys.data?.writeKeys ?? []}
      />
    </div>
  );
}

function CreateSchemaCard({
  csrfToken,
  onCreated,
  scope,
}: {
  csrfToken: string;
  onCreated: (schema: AnalyticsEventSchemaRecord) => Promise<void>;
  scope: AnalyticsScope;
}) {
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [retentionDays, setRetentionDays] = useState(90);
  const [schemaJson, setSchemaJson] = useState(defaultSchema);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const schema = await createAnalyticsSchema(csrfToken, scope, {
        key,
        displayName,
        description,
        retentionDays,
        schemaJson,
      });
      await onCreated(schema);
      setKey("");
      setDisplayName("");
      setDescription("");
      toast.success("Event schema created.");
    } catch (error) {
      toast.error(analyticsErrorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Create event schema</CardTitle>
        <CardDescription>
          Mark fields with x-asterloom-sensitive to replace their stored values with [REDACTED].
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <label className={analyticsLabelClassName}>Event name<input className={analyticsInputClassName} name="schemaKey" onChange={(event) => setKey(event.target.value)} placeholder="checkout.completed" required value={key} /></label>
          <label className={analyticsLabelClassName}>Display name<input className={analyticsInputClassName} name="schemaDisplayName" onChange={(event) => setDisplayName(event.target.value)} required value={displayName} /></label>
          <label className={analyticsLabelClassName}>Retention days<input className={analyticsInputClassName} min={1} max={3650} name="schemaRetention" onChange={(event) => setRetentionDays(event.target.valueAsNumber)} type="number" value={retentionDays} /></label>
          <label className={analyticsLabelClassName}>Description<input className={analyticsInputClassName} name="schemaDescription" onChange={(event) => setDescription(event.target.value)} value={description} /></label>
          <label className={cn(analyticsLabelClassName, "sm:col-span-2")}>JSON Schema<textarea className={cn(analyticsTextAreaClassName, "h-64")} name="schemaJson" onChange={(event) => setSchemaJson(event.target.value)} value={schemaJson} /></label>
          <div className="sm:col-span-2">
            <Button data-ui-action="create-analytics-schema" disabled={busy} type="submit">
              {busy ? <LoaderCircle className="size-4 animate-spin" /> : <Plus className="size-4" />}
              Create schema
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function SchemaInspector({
  csrfToken,
  onChanged,
  schema,
}: {
  csrfToken: string;
  onChanged: (schema: AnalyticsEventSchemaRecord) => Promise<void>;
  schema: AnalyticsEventSchemaRecord;
}) {
  const [busy, setBusy] = useState("");
  const [displayName, setDisplayName] = useState(schema.displayName);
  const [description, setDescription] = useState(schema.description);
  const [schemaJson, setSchemaJson] = useState(() => formatJson(schema.schemaJson));
  const [retentionDays, setRetentionDays] = useState(schema.retentionDays);
  const active = schema.status === AnalyticsResourceStatusObject.ANALYTICS_RESOURCE_STATUS_ACTIVE;

  async function perform(name: string, action: () => Promise<AnalyticsEventSchemaRecord>, message: string) {
    setBusy(name);
    try {
      await onChanged(await action());
      toast.success(message);
    } catch (error) {
      toast.error(analyticsErrorMessage(error));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>{schema.displayName}</CardTitle>
          <AnalyticsStatusBadge status={schema.status} />
        </div>
        <CardDescription className="font-mono">{schema.key}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <label className={analyticsLabelClassName}>Display name<input className={analyticsInputClassName} disabled={!active} name="editAnalyticsSchemaDisplayName" onChange={(event) => setDisplayName(event.target.value)} value={displayName} /></label>
        <label className={analyticsLabelClassName}>Description<textarea className={cn(analyticsTextAreaClassName, "h-20 font-sans")} disabled={!active} name="editAnalyticsSchemaDescription" onChange={(event) => setDescription(event.target.value)} value={description} /></label>
        <label className={analyticsLabelClassName}>JSON Schema<textarea className={cn(analyticsTextAreaClassName, "h-64")} disabled={!active} name="editAnalyticsSchemaJson" onChange={(event) => setSchemaJson(event.target.value)} value={schemaJson} /></label>
        {active ? (
          <div className="flex flex-wrap gap-2">
            <Button
              data-ui-action="update-analytics-schema"
              disabled={Boolean(busy)}
              onClick={() => void perform("save", () => updateAnalyticsSchema(csrfToken, schema, { displayName, description, schemaJson }), "Event schema updated.")}
              type="button"
            >
              <Save className="size-4" /> Save contract
            </Button>
            <Button
              data-ui-action="archive-analytics-schema"
              disabled={Boolean(busy)}
              onClick={() => void perform("archive", () => archiveAnalyticsSchema(csrfToken, schema), "Event schema archived.")}
              type="button"
              variant="outline"
            >
              <Archive className="size-4" /> Archive
            </Button>
          </div>
        ) : (
          <Button
            data-ui-action="restore-analytics-schema"
            disabled={Boolean(busy)}
            onClick={() => void perform("restore", () => restoreAnalyticsSchema(csrfToken, schema), "Event schema restored.")}
            type="button"
          >
            <RotateCcw className="size-4" /> Restore schema
          </Button>
        )}
        <div className="border-t border-white/8 pt-4">
          <label className={analyticsLabelClassName}>
            Retention days
            <div className="flex gap-2">
              <input className={analyticsInputClassName} max={3650} min={1} name="editAnalyticsRetention" onChange={(event) => setRetentionDays(event.target.valueAsNumber)} type="number" value={retentionDays} />
              <Button
                data-ui-action="update-analytics-retention"
                disabled={Boolean(busy)}
                onClick={() => void perform("retention", () => updateAnalyticsRetention(csrfToken, schema, retentionDays), "Retention policy updated.")}
                type="button"
                variant="outline"
              >
                Apply
              </Button>
            </div>
          </label>
        </div>
      </CardContent>
    </Card>
  );
}

function WriteKeysCard({
  csrfToken,
  error,
  isLoading,
  onChanged,
  scope,
  writeKeys,
}: {
  csrfToken: string;
  error: unknown;
  isLoading: boolean;
  onChanged: () => Promise<unknown>;
  scope: AnalyticsScope;
  writeKeys: AnalyticsWriteKeyRecord[];
}) {
  const [name, setName] = useState("");
  const [busy, setBusy] = useState("");
  const [credential, setCredential] = useState<AnalyticsWriteKeyCredential | null>(null);

  async function create(event: FormEvent) {
    event.preventDefault();
    setBusy("create");
    try {
      setCredential(await createAnalyticsWriteKey(csrfToken, scope, name));
      setName("");
      await onChanged();
      toast.success("Analytics write key created. Copy it now.");
    } catch (caught) {
      toast.error(analyticsErrorMessage(caught));
    } finally {
      setBusy("");
    }
  }

  async function perform(key: AnalyticsWriteKeyRecord, operation: "rotate" | "revoke") {
    setBusy(`${operation}:${key.id}`);
    try {
      if (operation === "rotate") {
        setCredential(await rotateAnalyticsWriteKey(csrfToken, key));
        toast.success("Write key rotated. Copy the replacement now.");
      } else {
        await revokeAnalyticsWriteKey(csrfToken, key);
        toast.success("Write key revoked.");
      }
      await onChanged();
    } catch (caught) {
      toast.error(analyticsErrorMessage(caught));
    } finally {
      setBusy("");
    }
  }

  return (
    <Card data-ui-action="list-analytics-write-keys">
      <CardHeader>
        <CardTitle>Environment write keys</CardTitle>
        <CardDescription>
          Secrets authenticate ingestion only. A newly created or rotated secret is shown once.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {credential && (
          <div className="rounded-xl border border-amber-300/25 bg-amber-300/[0.07] p-4">
            <p className="text-xs font-medium text-amber-200">Copy this secret now</p>
            <div className="mt-2 flex gap-2">
              <code className="min-w-0 flex-1 break-all rounded-lg bg-black/25 p-3 text-xs text-amber-100" data-testid="analytics-write-key-secret">{credential.secret}</code>
              <Button
                aria-label="Copy analytics write key"
                onClick={() => void navigator.clipboard.writeText(credential.secret).then(() => toast.success("Write key copied."))}
                size="sm"
                type="button"
                variant="outline"
              >
                <Clipboard className="size-3.5" />
              </Button>
            </div>
          </div>
        )}
        <form className="flex flex-col gap-3 sm:flex-row" onSubmit={create}>
          <label className={cn(analyticsLabelClassName, "flex-1")}>
            Key name
            <input className={analyticsInputClassName} onChange={(event) => setName(event.target.value)} placeholder="Production .NET SDK" required value={name} />
          </label>
          <Button className="sm:mt-[22px]" data-ui-action="create-analytics-write-key" disabled={Boolean(busy)} type="submit">
            <KeyRound className="size-4" /> Create write key
          </Button>
        </form>
        {isLoading ? (
          <AnalyticsLoading label="Loading write keys" />
        ) : error ? (
          <AnalyticsError error={error} />
        ) : writeKeys.length === 0 ? (
          <AnalyticsEmpty message="No analytics write keys exist." />
        ) : (
          <div className="grid gap-3 lg:grid-cols-2">
            {writeKeys.map((key) => {
              const active = key.status === AnalyticsWriteKeyStatusObject.ANALYTICS_WRITE_KEY_STATUS_ACTIVE;
              return (
                <article className="rounded-xl border border-white/8 bg-white/[0.02] p-4" data-testid={`analytics-write-key-${key.prefix}`} key={key.id}>
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-medium text-white">{key.name}</p>
                      <p className="mt-1 font-mono text-xs text-cyan-300">{key.prefix}</p>
                    </div>
                    <AnalyticsStatusBadge status={key.status} />
                  </div>
                  <p className="mt-3 text-xs text-slate-500">
                    Last used {key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleString() : "never"}
                  </p>
                  {active && (
                    <div className="mt-4 flex gap-2">
                      <Button data-ui-action="rotate-analytics-write-key" disabled={Boolean(busy)} onClick={() => void perform(key, "rotate")} size="sm" type="button" variant="outline">
                        <RefreshCw className="size-3.5" /> Rotate
                      </Button>
                      <Button data-ui-action="revoke-analytics-write-key" disabled={Boolean(busy)} onClick={() => void perform(key, "revoke")} size="sm" type="button" variant="outline">
                        <ShieldOff className="size-3.5" /> Revoke
                      </Button>
                    </div>
                  )}
                </article>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function formatJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
