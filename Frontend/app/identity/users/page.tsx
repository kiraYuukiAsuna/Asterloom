import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { IdentityWorkspace } from "@/features/identity/identity-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function IdentityUsersPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Fidentity%2Fusers");
  }

  return (
    <ConsoleShell
      activeRoute="/identity/users"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Global Passport accounts, application memberships, sessions, OIDC clients, and scopes"
      headerTitle="Identity"
    >
      <IdentityWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
