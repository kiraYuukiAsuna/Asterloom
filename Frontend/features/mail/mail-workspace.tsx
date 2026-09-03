"use client";

import {
  Archive,
  History,
  LoaderCircle,
  Mail,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Send,
  ServerCog,
} from "lucide-react";
import Link from "next/link";
import { useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";
import useSWR from "swr";

import { useLocale } from "@/components/i18n/locale-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  archiveSmtpAccount,
  createSmtpAccount,
  getMailDelivery,
  getSmtpAccount,
  listMailDeliveries,
  listSmtpAccounts,
  mailErrorMessage,
  parseMailAddresses,
  restoreSmtpAccount,
  sendMail,
  testSmtpAccount,
  updateSmtpAccount,
  type MailDeliveryRecord,
  type MailScope,
  type SmtpAccountRecord,
  type SmtpSecurity,
} from "@/lib/api/mail-management";
import { MailAccountStatusObject, MailDeliveryStatusObject, SmtpSecurityObject } from "@/lib/api/generated/models";
import { listApplications, listTenants } from "@/lib/api/platform-management";
import { useHydrated } from "@/lib/ui/use-hydrated";
import { cn } from "@/lib/utils/cn";

import { useMailSelection } from "./mail-store";

const page = { includeArchived: false, pageSize: 100, pageToken: "", query: "" };
const inputClassName = "h-10 w-full rounded-lg border border-white/10 bg-slate-950/75 px-3 text-sm text-slate-100 outline-none transition placeholder:text-slate-600 focus:border-sky-400/45 focus:ring-2 focus:ring-sky-400/15 disabled:opacity-50";
const textAreaClassName = cn(inputClassName, "h-28 resize-y py-2.5 leading-5");
const labelClassName = "grid gap-1.5 text-xs font-medium text-slate-400";

export function MailWorkspace({ csrfToken, view }: { csrfToken: string; view: "accounts" | "deliveries" }) {
  const { t } = useLocale();
  const hydrated = useHydrated();
  const selection = useMailSelection();
  const tenants = useSWR(hydrated ? "mail-scope-tenants" : null, () => listTenants(page));
  const applications = useSWR(
    selection.tenantId ? ["mail-scope-applications", selection.tenantId] : null,
    () => listApplications(selection.tenantId, page),
  );
  const scope = useMemo<MailScope | null>(
    () => selection.tenantId && selection.applicationId
      ? { tenantId: selection.tenantId, applicationId: selection.applicationId }
      : null,
    [selection.applicationId, selection.tenantId],
  );

  return (
    <div className="space-y-6" data-hydrated={hydrated ? "true" : "false"} data-mail-workspace>
      <section className="theme-hero-violet flex flex-col gap-5 rounded-2xl border border-sky-400/15 bg-gradient-to-br from-sky-400/[0.09] via-slate-950/60 to-violet-400/[0.05] p-6 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <Badge variant="info">{t("Application messaging")}</Badge>
          <h1 className="mt-4 text-2xl font-semibold tracking-tight text-white">{t("Mail control center")}</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-400">
            {t("Configure encrypted SMTP credentials and inspect application delivery; stored authorization codes are never returned by APIs or written to logs.")}
          </p>
        </div>
        <nav aria-label={t("Mail views")} className="flex rounded-xl border border-white/10 p-1">
          <MailTab active={view === "accounts"} href="/mail/accounts" icon={ServerCog} label={t("SMTP accounts")} />
          <MailTab active={view === "deliveries"} href="/mail/deliveries" icon={History} label={t("Compose & history")} />
        </nav>
      </section>

      <Card>
        <CardHeader className="sm:flex-row sm:items-end sm:justify-between">
          <div>
            <CardTitle>{t("Mail boundary")}</CardTitle>
            <CardDescription>{t("SMTP accounts and delivery history are isolated per tenant and application.")}</CardDescription>
          </div>
          <Button onClick={() => { void tenants.mutate(); void applications.mutate(); }} size="sm" type="button" variant="outline">
            <RefreshCw aria-hidden="true" className="size-3.5" /> {t("Refresh")}
          </Button>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <SearchableSelect ariaLabel={t("Mail tenant")} className={inputClassName} emptyLabel={t("Choose a tenant")} label={t("Tenant")} labelClassName={labelClassName} onChange={selection.selectTenant} options={(tenants.data?.tenants ?? []).map((tenant) => ({ label: `${tenant.displayName} (${tenant.slug})`, value: tenant.id }))} value={selection.tenantId} />
          <SearchableSelect ariaLabel={t("Mail application")} className={inputClassName} disabled={!selection.tenantId} emptyLabel={t("Choose an application")} label={t("Application")} labelClassName={labelClassName} onChange={selection.selectApplication} options={(applications.data?.applications ?? []).map((application) => ({ label: `${application.displayName} (${application.slug})`, value: application.id }))} value={selection.applicationId} />
          {(tenants.error ?? applications.error) && <div className="md:col-span-2"><MailError error={tenants.error ?? applications.error} /></div>}
        </CardContent>
      </Card>

      {!scope ? <MailEmpty message={t("Choose a tenant and application to manage mail.")} />
        : view === "accounts" ? <AccountsPanel csrfToken={csrfToken} scope={scope} />
        : <DeliveriesPanel csrfToken={csrfToken} scope={scope} />}
    </div>
  );
}

function AccountsPanel({ csrfToken, scope }: { csrfToken: string; scope: MailScope }) {
  const { t } = useLocale();
  const [includeArchived, setIncludeArchived] = useState(false);
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<SmtpAccountRecord | null>(null);
  const accounts = useSWR(
    ["mail-accounts", scope.tenantId, scope.applicationId, includeArchived, query],
    () => listSmtpAccounts(scope, { includeArchived, pageSize: 100, query }),
  );

  async function load(account: SmtpAccountRecord) {
    try {
      setSelected(await getSmtpAccount(scope, account.id));
    } catch (error) {
      toast.error(t(mailErrorMessage(error)));
    }
  }

  async function refresh(account?: SmtpAccountRecord) {
    await accounts.mutate();
    if (account) setSelected(account);
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1.05fr)_minmax(24rem,.95fr)]">
      <div className="space-y-6">
        <CreateAccountCard csrfToken={csrfToken} onCreated={refresh} scope={scope} />
        <Card data-ui-action="list-smtp-accounts">
          <CardHeader>
            <CardTitle>{t("SMTP accounts")}</CardTitle>
            <CardDescription>{t("Authorization codes are write-only and encrypted with the server data-protection key ring.")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-col gap-3 sm:flex-row">
              <label className="relative flex-1">
                <Search aria-hidden="true" className="absolute left-3 top-3 size-4 text-slate-600" />
                <input className={cn(inputClassName, "pl-9")} onChange={(event) => setQuery(event.target.value)} placeholder={t("Search SMTP accounts")} value={query} />
              </label>
              <label className="flex items-center gap-2 text-xs text-slate-400">
                <input checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} type="checkbox" />
                {t("Include archived")}
              </label>
            </div>
            {accounts.isLoading ? <MailLoading label={t("Loading SMTP accounts…")} />
              : accounts.error ? <MailError error={accounts.error} />
              : accounts.data?.accounts.length === 0 ? <MailEmpty message={t("No SMTP accounts match this scope.")} />
              : <div className="space-y-2">{accounts.data?.accounts.map((account) => (
                <button
                  className={cn("flex w-full items-center justify-between rounded-xl border p-4 text-left transition", selected?.id === account.id ? "border-sky-400/35 bg-sky-400/[0.08]" : "border-white/8 bg-white/[0.02] hover:bg-white/[0.05]")}
                  data-ui-action="get-smtp-account"
                  key={account.id}
                  onClick={() => void load(account)}
                  type="button"
                >
                  <span><span className="block text-sm font-medium text-slate-100">{account.name}</span><span className="mt-1 block text-xs text-slate-500">{account.fromAddress} · {account.host}:{account.port}</span></span>
                  <MailStatusBadge status={account.status} />
                </button>
              ))}</div>}
          </CardContent>
        </Card>
      </div>
      {selected ? <AccountDetailCard account={selected} csrfToken={csrfToken} key={`${selected.id}:${selected.version}`} onChanged={refresh} />
        : <MailEmpty message={t("Select an SMTP account to edit, test, archive, or restore it.")} />}
    </div>
  );
}

function CreateAccountCard({ csrfToken, onCreated, scope }: { csrfToken: string; onCreated: (account: SmtpAccountRecord) => Promise<void>; scope: MailScope }) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState("QQ Mail");
  const [host, setHost] = useState("smtp.qq.com");
  const [port, setPort] = useState("465");
  const [security, setSecurity] = useState<SmtpSecurity>(SmtpSecurityObject.SMTP_SECURITY_SSL_ON_CONNECT);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [fromAddress, setFromAddress] = useState("");
  const [fromName, setFromName] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      const account = await createSmtpAccount(csrfToken, scope, { name, host, port: Number(port), security, username, smtpPassword: password, fromAddress, fromName });
      setPassword("");
      toast.success(t("SMTP account created."));
      await onCreated(account);
    } catch (error) {
      toast.error(t(mailErrorMessage(error)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card data-ui-action="create-smtp-account">
      <CardHeader><CardTitle>{t("Add SMTP account")}</CardTitle><CardDescription>{t("For QQ Mail, enable SMTP and enter the generated authorization code instead of the QQ password.")}</CardDescription></CardHeader>
      <CardContent>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={submit}>
          <MailInput label={t("Account name")} onChange={setName} value={name} />
          <MailInput label={t("SMTP host")} onChange={setHost} value={host} />
          <MailInput label={t("Port")} onChange={setPort} type="number" value={port} />
          <label className={labelClassName}>{t("Transport security")}<select aria-label={t("Transport security")} className={inputClassName} onChange={(event) => setSecurity(event.target.value as SmtpSecurity)} value={security}>
            <option value={SmtpSecurityObject.SMTP_SECURITY_SSL_ON_CONNECT}>{t("SSL/TLS on connect (465)")}</option>
            <option value={SmtpSecurityObject.SMTP_SECURITY_START_TLS}>{t("STARTTLS (587)")}</option>
          </select></label>
          <MailInput label={t("SMTP username")} onChange={setUsername} value={username} />
          <MailInput autoComplete="new-password" label={t("Authorization code / password")} onChange={setPassword} type="password" value={password} />
          <MailInput label={t("From address")} onChange={setFromAddress} type="email" value={fromAddress} />
          <MailInput label={t("From name")} onChange={setFromName} value={fromName} />
          <Button className="sm:col-span-2" data-ui-action="create-smtp-account" disabled={busy} type="submit"><Plus className="size-4" />{busy ? t("Creating…") : t("Create SMTP account")}</Button>
        </form>
      </CardContent>
    </Card>
  );
}

function AccountDetailCard({ account, csrfToken, onChanged }: { account: SmtpAccountRecord; csrfToken: string; onChanged: (account: SmtpAccountRecord) => Promise<void> }) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState(account.name);
  const [host, setHost] = useState(account.host);
  const [port, setPort] = useState(String(account.port));
  const [security, setSecurity] = useState(account.security);
  const [username, setUsername] = useState(account.username);
  const [password, setPassword] = useState("");
  const [fromAddress, setFromAddress] = useState(account.fromAddress);
  const [fromName, setFromName] = useState(account.fromName);
  const [recipient, setRecipient] = useState(account.fromAddress);
  const archived = account.status === MailAccountStatusObject.MAIL_ACCOUNT_STATUS_ARCHIVED;

  async function run(operation: () => Promise<SmtpAccountRecord>, success: string) {
    setBusy(true);
    try {
      const updated = await operation();
      toast.success(t(success));
      await onChanged(updated);
    } catch (error) { toast.error(t(mailErrorMessage(error))); } finally { setBusy(false); }
  }

  return (
    <Card className="h-fit xl:sticky xl:top-24">
      <CardHeader><div className="flex items-center justify-between gap-3"><CardTitle>{account.name}</CardTitle><MailStatusBadge status={account.status} /></div><CardDescription>{t("Leave the authorization code blank to keep the stored credential unchanged.")}</CardDescription></CardHeader>
      <CardContent className="space-y-5">
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={(event) => { event.preventDefault(); void run(() => updateSmtpAccount(csrfToken, account, { name, host, port: Number(port), security, username, smtpPassword: password, fromAddress, fromName }), "SMTP account updated."); }}>
          <MailInput disabled={archived} label={t("Account name")} onChange={setName} value={name} />
          <MailInput disabled={archived} label={t("SMTP host")} onChange={setHost} value={host} />
          <MailInput disabled={archived} label={t("Port")} onChange={setPort} type="number" value={port} />
          <label className={labelClassName}>{t("Transport security")}<select aria-label={t("Transport security")} className={inputClassName} disabled={archived} onChange={(event) => setSecurity(event.target.value as SmtpSecurity)} value={security}><option value={SmtpSecurityObject.SMTP_SECURITY_SSL_ON_CONNECT}>{t("SSL/TLS on connect (465)")}</option><option value={SmtpSecurityObject.SMTP_SECURITY_START_TLS}>{t("STARTTLS (587)")}</option></select></label>
          <MailInput disabled={archived} label={t("SMTP username")} onChange={setUsername} value={username} />
          <MailInput autoComplete="new-password" disabled={archived} label={t("New authorization code") } onChange={setPassword} type="password" value={password} />
          <MailInput disabled={archived} label={t("From address")} onChange={setFromAddress} type="email" value={fromAddress} />
          <MailInput disabled={archived} label={t("From name")} onChange={setFromName} value={fromName} />
          {!archived && <Button className="sm:col-span-2" data-ui-action="update-smtp-account" disabled={busy} type="submit">{t("Save SMTP account")}</Button>}
        </form>
        {!archived && <form className="space-y-3 border-t border-white/8 pt-5" onSubmit={(event) => { event.preventDefault(); setBusy(true); void testSmtpAccount(csrfToken, account, recipient).then((delivery) => { if (delivery.status === MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_SENT) toast.success(t("Test email sent.")); else toast.error(t(delivery.errorMessage)); }).catch((error) => toast.error(t(mailErrorMessage(error)))).finally(() => setBusy(false)); }}>
          <MailInput label={t("Test recipient")} onChange={setRecipient} type="email" value={recipient} />
          <Button className="w-full" data-ui-action="test-smtp-account" disabled={busy} type="submit" variant="outline"><Send className="size-4" />{t("Send test email")}</Button>
        </form>}
        {archived ? (
          <Button className="w-full" data-ui-action="restore-smtp-account" disabled={busy} onClick={() => void run(() => restoreSmtpAccount(csrfToken, account), "SMTP account restored.")} type="button" variant="outline">
            <RotateCcw className="size-4" />{t("Restore SMTP account")}
          </Button>
        ) : (
          <Button className="w-full" data-ui-action="archive-smtp-account" disabled={busy} onClick={() => void run(() => archiveSmtpAccount(csrfToken, account), "SMTP account archived.")} type="button" variant="outline">
            <Archive className="size-4" />{t("Archive SMTP account")}
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

function DeliveriesPanel({ csrfToken, scope }: { csrfToken: string; scope: MailScope }) {
  const { t } = useLocale();
  const [status, setStatus] = useState("");
  const [selected, setSelected] = useState<MailDeliveryRecord | null>(null);
  const accounts = useSWR(["mail-compose-accounts", scope.tenantId, scope.applicationId], () => listSmtpAccounts(scope, { pageSize: 100 }));
  const deliveries = useSWR(["mail-deliveries", scope.tenantId, scope.applicationId, status], () => listMailDeliveries(scope, { pageSize: 100, status: status ? status as MailDeliveryRecord["status"] : undefined }));

  async function load(delivery: MailDeliveryRecord) {
    try { setSelected(await getMailDelivery(scope, delivery.id)); } catch (error) { toast.error(t(mailErrorMessage(error))); }
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(24rem,.9fr)_minmax(0,1.1fr)]">
      <ComposeCard accounts={accounts.data?.accounts ?? []} csrfToken={csrfToken} onSent={async (delivery) => { setSelected(delivery); await deliveries.mutate(); }} scope={scope} />
      <div className="space-y-6">
        <Card data-ui-action="list-mail-deliveries">
          <CardHeader className="sm:flex-row sm:items-end sm:justify-between"><div><CardTitle>{t("Delivery history")}</CardTitle><CardDescription>{t("Message bodies and SMTP credentials are not retained in delivery history.")}</CardDescription></div><select className={cn(inputClassName, "sm:w-44")} onChange={(event) => setStatus(event.target.value)} value={status}><option value="">{t("All statuses")}</option><option value={MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_SENT}>{t("Sent")}</option><option value={MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_FAILED}>{t("Failed")}</option><option value={MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_PENDING}>{t("Pending")}</option></select></CardHeader>
          <CardContent>
            {deliveries.isLoading ? <MailLoading label={t("Loading delivery history…")} /> : deliveries.error ? <MailError error={deliveries.error} /> : deliveries.data?.deliveries.length === 0 ? <MailEmpty message={t("No email deliveries have been recorded.")} /> : <div className="space-y-2">{deliveries.data?.deliveries.map((delivery) => <button className="flex w-full items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/[0.02] p-4 text-left hover:bg-white/[0.05]" data-ui-action="get-mail-delivery" key={delivery.id} onClick={() => void load(delivery)} type="button"><span className="min-w-0"><span className="block truncate text-sm font-medium text-slate-100">{delivery.subject}</span><span className="mt-1 block truncate text-xs text-slate-500">{delivery.to.join(", ")} · {formatDate(delivery.createdAt)}</span></span><MailStatusBadge status={delivery.status} /></button>)}</div>}
          </CardContent>
        </Card>
        {selected && <DeliveryDetail delivery={selected} />}
      </div>
    </div>
  );
}

function ComposeCard({ accounts, csrfToken, onSent, scope }: { accounts: SmtpAccountRecord[]; csrfToken: string; onSent: (delivery: MailDeliveryRecord) => Promise<void>; scope: MailScope }) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [accountId, setAccountId] = useState("");
  const [to, setTo] = useState("");
  const [cc, setCc] = useState("");
  const [bcc, setBcc] = useState("");
  const [replyTo, setReplyTo] = useState("");
  const [subject, setSubject] = useState("");
  const [textBody, setTextBody] = useState("");
  const [htmlBody, setHtmlBody] = useState("");
  const [clientMessageId, setClientMessageId] = useState(() => `console:${globalThis.crypto?.randomUUID?.() ?? Date.now()}`);

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true);
    try {
      const delivery = await sendMail(csrfToken, scope, { smtpAccountId: accountId, clientMessageId, to: parseMailAddresses(to), cc: parseMailAddresses(cc), bcc: parseMailAddresses(bcc), replyTo, subject, textBody, htmlBody });
      if (delivery.status === MailDeliveryStatusObject.MAIL_DELIVERY_STATUS_SENT) toast.success(t("Email sent."));
      else toast.error(t(delivery.errorMessage || "Email delivery failed."));
      setClientMessageId(`console:${crypto.randomUUID()}`);
      await onSent(delivery);
    } catch (error) { toast.error(t(mailErrorMessage(error))); } finally { setBusy(false); }
  }

  return <Card className="h-fit xl:sticky xl:top-24"><CardHeader><CardTitle>{t("Compose application email")}</CardTitle><CardDescription>{t("This uses the same authenticated Mail API exposed to confidential business backends.")}</CardDescription></CardHeader><CardContent><form className="space-y-4" onSubmit={submit}>
    <label className={labelClassName}>{t("SMTP account")}<select className={inputClassName} onChange={(event) => setAccountId(event.target.value)} required value={accountId}><option value="">{t("Choose an SMTP account")}</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.name} · {account.fromAddress}</option>)}</select></label>
    <MailInput label={t("To (comma, semicolon, or newline separated)")} onChange={setTo} value={to} />
    <div className="grid gap-4 sm:grid-cols-2"><MailInput label={t("CC")} onChange={setCc} value={cc} /><MailInput label={t("BCC")} onChange={setBcc} value={bcc} /></div>
    <MailInput label={t("Reply-To")} onChange={setReplyTo} type="email" value={replyTo} />
    <MailInput label={t("Subject")} onChange={setSubject} value={subject} />
    <label className={labelClassName}>{t("Text body")}<textarea className={textAreaClassName} onChange={(event) => setTextBody(event.target.value)} value={textBody} /></label>
    <label className={labelClassName}>{t("HTML body")}<textarea className={cn(textAreaClassName, "font-mono text-xs")} onChange={(event) => setHtmlBody(event.target.value)} value={htmlBody} /></label>
    <MailInput label={t("Client message ID (idempotency key)")} onChange={setClientMessageId} value={clientMessageId} />
    <Button className="w-full" data-ui-action="send-email" disabled={busy || accounts.length === 0} type="submit"><Send className="size-4" />{busy ? t("Sending…") : t("Send email")}</Button>
  </form></CardContent></Card>;
}

function DeliveryDetail({ delivery }: { delivery: MailDeliveryRecord }) {
  const { t } = useLocale();
  return <Card><CardHeader><div className="flex items-center justify-between gap-3"><CardTitle>{delivery.subject}</CardTitle><MailStatusBadge status={delivery.status} /></div><CardDescription>{delivery.clientMessageId}</CardDescription></CardHeader><CardContent className="grid gap-3 text-sm sm:grid-cols-2"><Detail label={t("To")} value={delivery.to.join(", ")} /><Detail label={t("CC")} value={delivery.cc.join(", ") || "—"} /><Detail label={t("BCC")} value={delivery.bcc.join(", ") || "—"} /><Detail label={t("Reply-To")} value={delivery.replyTo || "—"} /><Detail label={t("Created")} value={formatDate(delivery.createdAt)} /><Detail label={t("Completed")} value={formatDate(delivery.completedAt)} /><Detail label={t("Provider message ID")} value={delivery.providerMessageId || "—"} /><Detail label={t("Error")} value={delivery.errorCode ? `${delivery.errorCode}: ${delivery.errorMessage}` : "—"} /></CardContent></Card>;
}

function MailTab({ active, href, icon: Icon, label }: { active: boolean; href: string; icon: typeof Mail; label: string }) { return <Link className={cn("flex h-9 items-center gap-2 rounded-lg px-3 text-xs font-medium transition", active ? "bg-sky-400/15 text-sky-100" : "text-slate-500 hover:bg-white/[0.04] hover:text-slate-200")} href={href}><Icon className="size-3.5" />{label}</Link>; }
function MailInput({ autoComplete, disabled, label, onChange, type = "text", value }: { autoComplete?: string; disabled?: boolean; label: string; onChange: (value: string) => void; type?: string; value: string }) { return <label className={labelClassName}>{label}<input autoComplete={autoComplete} className={inputClassName} disabled={disabled} onChange={(event) => onChange(event.target.value)} type={type} value={value} /></label>; }
function MailStatusBadge({ status }: { status: string }) { const { t } = useLocale(); const label = status.replace(/^(MAIL_ACCOUNT_STATUS_|MAIL_DELIVERY_STATUS_)/u, "").replaceAll("_", " ").toLowerCase(); return <Badge variant={status.endsWith("_ACTIVE") || status.endsWith("_SENT") ? "success" : status.endsWith("_FAILED") ? "planned" : "info"}>{t(label)}</Badge>; }
function MailLoading({ label }: { label: string }) { return <div className="flex items-center justify-center gap-2 rounded-xl border border-white/8 bg-white/[0.02] p-8 text-sm text-slate-500"><LoaderCircle className="size-4 animate-spin" />{label}</div>; }
function MailEmpty({ message }: { message: string }) { return <Card><CardContent className="p-8 text-center text-sm text-slate-500">{message}</CardContent></Card>; }
function MailError({ error }: { error: unknown }) { const { t } = useLocale(); return <div className="rounded-xl border border-rose-400/20 bg-rose-400/[0.06] p-4 text-sm text-rose-200">{t(mailErrorMessage(error))}</div>; }
function Detail({ label, value }: { label: string; value: string }) { return <div className="rounded-lg border border-white/8 bg-white/[0.02] p-3"><span className="block text-xs text-slate-500">{label}</span><span className="mt-1 block break-words text-slate-200">{value}</span></div>; }
function formatDate(value: string | null) { return value ? new Date(value).toLocaleString() : "—"; }
