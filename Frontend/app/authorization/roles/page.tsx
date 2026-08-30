import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { AuthorizationWorkspace } from "@/features/authorization/authorization-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function AuthorizationRolesPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Fauthorization%2Froles");
  }

  return (
    <ConsoleShell
      activeRoute="/authorization/roles"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Roles, scoped bindings, policy rules, revisions, and decision simulation"
      headerTitle="Authorization"
    >
      <AuthorizationWorkspace
        actorId={session.record.actor.subject}
        csrfToken={session.record.csrfToken}
      />
    </ConsoleShell>
  );
}
