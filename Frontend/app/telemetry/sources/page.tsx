import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { TelemetryWorkspace } from "@/features/telemetry/telemetry-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function TelemetrySourcesPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Ftelemetry%2Fsources");
  return (
    <ConsoleShell activeRoute="/telemetry/health" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Service resources, signal policy, sampling, and OTLP export" headerTitle="Telemetry sources">
      <TelemetryWorkspace csrfToken={session.record.csrfToken} view="sources" />
    </ConsoleShell>
  );
}
