import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { AnalyticsWorkspace } from "@/features/analytics/analytics-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function AnalyticsExplorerPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Fanalytics%2Fexplorer");

  return (
    <ConsoleShell
      activeRoute="/analytics/explorer"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Schema-governed product events, outcome queries, and controlled exports"
      headerTitle="Analytics explorer"
    >
      <AnalyticsWorkspace csrfToken={session.record.csrfToken} view="explorer" />
    </ConsoleShell>
  );
}
