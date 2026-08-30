import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { OperationsWorkspace } from "@/features/operations/operations-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function OperationsApisPage() {
  const session = await readCurrentSession();
  if (!session) redirect("/login?returnTo=%2Foperations%2Fapis");
  return <ConsoleShell activeRoute="/operations/apis" actor={session.record.actor} csrfToken={session.record.csrfToken} headerDescription="Live gRPC, JSON Transcoding, and OpenAPI surface" headerTitle="API operations"><OperationsWorkspace view="apis" /></ConsoleShell>;
}
