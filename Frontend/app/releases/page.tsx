import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { ReleaseWorkspace } from "@/features/release/release-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function ReleasesPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Freleases");

  return (
    <ConsoleShell
      activeRoute="/releases"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Signed manifests, deterministic rollout, pause, promotion, and rollback"
      headerTitle="Desktop releases"
    >
      <ReleaseWorkspace csrfToken={session.record.csrfToken} view="releases" />
    </ConsoleShell>
  );
}
