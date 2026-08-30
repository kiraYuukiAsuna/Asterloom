import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { AuditWorkspace } from "@/features/audit/audit-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function AuditPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Faudit");
  }

  return (
    <ConsoleShell
      activeRoute="/audit"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Immutable administrative outcomes, scope, and request correlation"
      headerTitle="Audit trail"
    >
      <AuditWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
