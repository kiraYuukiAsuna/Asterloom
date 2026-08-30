import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { AnalyticsWorkspace } from "@/features/analytics/analytics-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function AnalyticsSchemasPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Fanalytics%2Fschemas");

  return (
    <ConsoleShell
      activeRoute="/analytics/explorer"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Event contracts, redaction, write keys, and retention governance"
      headerTitle="Analytics schemas"
    >
      <AnalyticsWorkspace csrfToken={session.record.csrfToken} view="schemas" />
    </ConsoleShell>
  );
}
