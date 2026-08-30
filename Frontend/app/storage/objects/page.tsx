import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { StorageWorkspace } from "@/features/storage/storage-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function StorageObjectsPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Fstorage%2Fobjects");
  }

  return (
    <ConsoleShell
      activeRoute="/storage/objects"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Verified upload, ticketed download, metadata, copy, and deletion"
      headerTitle="File storage"
    >
      <StorageWorkspace csrfToken={session.record.csrfToken} view="objects" />
    </ConsoleShell>
  );
}
