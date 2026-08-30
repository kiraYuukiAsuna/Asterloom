import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { ReleaseWorkspace } from "@/features/release/release-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function ArtifactsPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Fartifacts");

  return (
    <ConsoleShell
      activeRoute="/releases"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Public trust keys, ticketed uploads, and artifact verification"
      headerTitle="Release artifacts"
    >
      <ReleaseWorkspace csrfToken={session.record.csrfToken} view="artifacts" />
    </ConsoleShell>
  );
}
