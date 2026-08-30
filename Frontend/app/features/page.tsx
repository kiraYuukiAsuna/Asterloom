import { redirect } from "next/navigation";

import { ConsoleShell } from "@/components/layout/console-shell";
import { FeatureWorkspace } from "@/features/feature/feature-workspace";
import { readCurrentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";

export default async function FeaturesPage() {
  const session = await readCurrentSession();
  if (!session) {
    redirect("/login?returnTo=%2Ffeatures");
  }

  return (
    <ConsoleShell
      activeRoute="/features"
      actor={session.record.actor}
      csrfToken={session.record.csrfToken}
      headerDescription="Typed drafts, immutable revisions, rollout, and runtime evaluation"
      headerTitle="Feature flags"
    >
      <FeatureWorkspace csrfToken={session.record.csrfToken} />
    </ConsoleShell>
  );
}
