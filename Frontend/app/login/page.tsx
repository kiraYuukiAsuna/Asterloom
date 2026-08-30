import { ArrowRight, KeyRound, RadioTower, ShieldCheck } from "lucide-react";
import { redirect } from "next/navigation";

import { Badge } from "@/components/ui/badge";
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

  return (
    <main className="relative grid min-h-dvh overflow-hidden px-5 py-8 lg:grid-cols-[1.15fr_0.85fr] lg:items-center lg:px-14">
      <div className="pointer-events-none absolute -left-40 top-[-16rem] size-[42rem] rounded-full bg-sky-500/10 blur-3xl" />
      <section className="relative mx-auto w-full max-w-2xl py-12 lg:mx-0">
        <div className="flex items-center gap-3">
          <div className="grid size-11 place-items-center rounded-2xl border border-sky-300/20 bg-sky-400/10 text-sky-300 shadow-2xl shadow-sky-950">
            <RadioTower aria-hidden="true" className="size-5" />
          </div>
          <div>
            <p className="font-semibold tracking-tight text-white">Asterloom</p>
            <p className="text-[10px] uppercase tracking-[0.2em] text-slate-500">
              Unified control plane
            </p>
          </div>
        </div>
        <Badge className="mt-16" variant="info">
          Passport protected
        </Badge>
        <h1 className="mt-6 max-w-xl text-5xl font-semibold tracking-[-0.055em] text-white sm:text-7xl">
          Every capability,
          <span className="block text-sky-300">one secure session.</span>
        </h1>
        <p className="mt-6 max-w-xl text-sm leading-7 text-slate-400 sm:text-base">
          Sign in through Asterloom Passport to manage applications, releases,
          configuration, telemetry, and every platform capability from one place.
        </p>
      </section>

      <section className="relative mx-auto w-full max-w-md rounded-3xl border border-white/10 bg-slate-950/70 p-6 shadow-[0_32px_100px_-48px_rgba(56,189,248,0.55)] backdrop-blur-2xl sm:p-8">
        <div className="grid size-12 place-items-center rounded-2xl bg-emerald-400/10 text-emerald-300">
          <KeyRound aria-hidden="true" className="size-5" />
        </div>
        <p className="mt-8 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
          Web Console
        </p>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight text-white">
          Continue with Passport
        </h2>
        <p className="mt-3 text-sm leading-6 text-slate-400">
          Authorization Code with S256 PKCE. Tokens remain inside the BFF and are
          never exposed to browser JavaScript.
        </p>

        {hasError && (
          <div className="mt-6 rounded-xl border border-rose-400/20 bg-rose-400/8 px-4 py-3 text-sm text-rose-200" role="alert">
            Passport could not complete the sign-in. Please try again.
          </div>
        )}
        {loggedOut && (
          <div className="mt-6 rounded-xl border border-emerald-400/20 bg-emerald-400/8 px-4 py-3 text-sm text-emerald-200" role="status">
            You have signed out successfully.
          </div>
        )}

        <a
          className="mt-8 flex h-12 items-center justify-center gap-2 rounded-xl bg-sky-300 px-4 text-sm font-bold text-slate-950 transition-colors hover:bg-sky-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-300"
          data-ui-action="start-passport-login"
          href={"/api/auth/login?returnTo=" + encodeURIComponent(returnTo)}
        >
          Sign in securely
          <ArrowRight aria-hidden="true" className="size-4" />
        </a>

        <div className="mt-6 flex items-start gap-3 border-t border-white/8 pt-6 text-xs leading-5 text-slate-500">
          <ShieldCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-emerald-400" />
          <p>
            Your browser receives only a random, HttpOnly session identifier.
            Mutations also require same-origin CSRF verification.
          </p>
        </div>
      </section>
    </main>
  );
}
