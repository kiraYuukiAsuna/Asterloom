import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { TelemetryWorkspace } from "@/features/telemetry/telemetry-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function TelemetryHealthPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Ftelemetry%2Fhealth");
  return (
    <ConsoleShell activeRoute="/telemetry/health" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Collector availability, recent technical failures, and trace pivots" headerTitle="Telemetry health">
      <TelemetryWorkspace csrfToken={session.record.csrfToken} view="health" />
    </ConsoleShell>
  );
}
