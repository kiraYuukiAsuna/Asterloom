import { ConsoleShell } from "@/components/layout/console-shell";
import { PlatformOverview } from "@/features/platform/platform-overview";
import { readCurrentSession } from "@/lib/auth/session";
import { redirect } from "next/navigation";

export const dynamic = "force-dynamic";

export default async function Home() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login");
  }

  return (
    <ConsoleShell
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
    >
      <PlatformOverview />
    </ConsoleShell>
  );
}
