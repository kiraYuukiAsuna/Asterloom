import { redirect } from "next/navigation";

import { LoginView } from "@/features/auth/login-view";
import { safeReturnTo } from "@/lib/auth/config";
import { readCurrentSession } from "@/lib/auth/session";

export const metadata = {
  title: "Sign in",
};

export const dynamic = "force-dynamic";

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const returnTo = safeReturnTo(
    typeof query.returnTo === "string" ? query.returnTo : null,
  );
  if (await readCurrentSession()) {
    redirect(returnTo);
  }
  const hasError = typeof query.error === "string";
  const loggedOut = query.loggedOut === "1";

  return <LoginView hasError={hasError} loggedOut={loggedOut} returnTo={returnTo} />;
}
