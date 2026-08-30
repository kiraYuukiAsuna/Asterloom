import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { TargetingWorkspace } from "@/features/targeting/targeting-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function TargetingSegmentsPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Ftargeting%2Fsegments");
  }

  return (
    <ConsoleShell
      activeRoute="/targeting/segments"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Typed audience rules, deterministic bucketing, and evaluation simulation"
      headerTitle="Targeting"
    >
      <TargetingWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
