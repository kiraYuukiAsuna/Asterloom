import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { ConfigWorkspace } from "@/features/config/config-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function ConfigPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Fconfig");
  }

  return (
    <ConsoleShell
      activeRoute="/config"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Typed values, JSON Schema, immutable snapshots, and ETag delivery"
      headerTitle="Dynamic configuration"
    >
      <ConfigWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
