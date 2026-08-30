import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { PlatformWorkspace } from "@/features/platform/platform-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function TenantsPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Ftenants");
  }

  return (
    <ConsoleShell
      activeRoute="/tenants"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Tenants, applications, environments, and memberships"
      headerTitle="Platform resources"
    >
      <PlatformWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
