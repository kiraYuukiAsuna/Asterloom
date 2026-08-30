import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { StorageWorkspace } from "@/features/storage/storage-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function StorageBucketsPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Fstorage%2Fbuckets");
  }

  return (
    <ConsoleShell
      activeRoute="/storage/objects"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Quotas, content policies, access boundaries, and bucket lifecycle"
      headerTitle="File storage"
    >
      <StorageWorkspace csrfToken={session.record.csrfToken} view="buckets" />
    </ConsoleShell>
  );
}
