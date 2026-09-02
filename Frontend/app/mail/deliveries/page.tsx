import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { MailWorkspace } from "@/features/mail/mail-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function MailDeliveriesPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Fmail%2Fdeliveries");
  return <ConsoleShell activeRoute="/mail/accounts" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Compose, test, and inspect application email" headerTitle="Mail"><MailWorkspace csrfToken={session.record.csrfToken} view="deliveries" /></ConsoleShell>;
}
