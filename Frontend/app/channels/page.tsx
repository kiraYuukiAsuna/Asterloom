import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { ReleaseWorkspace } from "@/features/release/release-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function ChannelsPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Fchannels");

  return (
    <ConsoleShell
      activeRoute="/releases"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Client-facing update routes and active release pointers"
      headerTitle="Release channels"
    >
      <ReleaseWorkspace csrfToken={session.record.csrfToken} view="channels" />
    </ConsoleShell>
  );
}
