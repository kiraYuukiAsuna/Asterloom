"use client";

import { useEffect } from "react";

import { useLocale } from "@/components/i18n/locale-provider";
import { Button } from "@/components/ui/button";

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const { t } = useLocale();
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <main className="grid min-h-dvh place-items-center p-6">
      <div className="max-w-md rounded-2xl border border-rose-400/15 bg-slate-950/70 p-8 text-center">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-rose-300">
          {t("Unexpected error")}
        </p>
        <h1 className="mt-3 text-2xl font-semibold text-white">
          {t("The console could not render this view.")}
        </h1>
        <p className="mt-3 text-sm leading-6 text-slate-400">
          {t("Retry the request. If it continues to fail, use the request trace to inspect Asterloom.Server.")}
        </p>
        <Button className="mt-6" onClick={reset} type="button">
          {t("Try again")}
        </Button>
      </div>
    </main>
  );
}
