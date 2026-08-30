import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { OperationsWorkspace } from "@/features/operations/operations-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function OperationsHealthPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Foperations%2Fhealth");
  return <ConsoleShell activeRoute="/operations/apis" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Readiness, startup, and registered dependency details" headerTitle="Platform health"><OperationsWorkspace view="health" /></ConsoleShell>;
}
