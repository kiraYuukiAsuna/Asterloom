import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { TelemetryWorkspace } from "@/features/telemetry/telemetry-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function TelemetrySignalsPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Ftelemetry%2Fsignals");
  return (
    <ConsoleShell activeRoute="/telemetry/health" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Traces, metric points, and logs stored in PostgreSQL" headerTitle="Telemetry signals">
      <TelemetryWorkspace csrfToken={session.record.csrfToken} view="signals" />
    </ConsoleShell>
  );
}
