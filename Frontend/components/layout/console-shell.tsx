"use client";

import {
  Activity,
  Boxes,
  Braces,
  Building2,
  ChartNoAxesCombined,
  Flag,
  HardDrive,
  LayoutDashboard,
  PackageOpen,
  RadioTower,
  ScrollText,
  ShieldCheck,
  SlidersHorizontal,
  Users,
} from "lucide-react";
import Link from "next/link";
import { useEffect, type ReactNode } from "react";

import { CommandMenu } from "@/components/layout/command-menu";
import { AccountMenu } from "@/components/layout/account-menu";
import { LocaleToggle } from "@/components/i18n/locale-toggle";
import { useLocale } from "@/components/i18n/locale-provider";
import { ThemeToggle } from "@/components/theme/theme-toggle";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils/cn";
import type { Actor } from "@/lib/auth/types";

const navigation = [
  { label: "Overview", href: "/", icon: LayoutDashboard, available: true },
  { label: "Tenants", href: "/tenants", icon: Building2, available: true },
  { label: "Identity", href: "/identity/users", icon: Users, available: true },
  {
    label: "Authorization",
    href: "/authorization/roles",
    icon: ShieldCheck,
    available: true,
  },
  { label: "Audit", href: "/audit", icon: ScrollText, available: true },
  { label: "Feature flags", href: "/features", icon: Flag, available: true },
  {
    label: "Targeting",
    href: "/targeting/segments",
    icon: SlidersHorizontal,
    available: true,
  },
  { label: "Configuration", href: "/config", icon: Boxes, available: true },
  { label: "Releases", href: "/releases", icon: PackageOpen, available: true },
  {
    label: "Analytics",
    href: "/analytics/explorer",
    icon: ChartNoAxesCombined,
    available: true,
  },
  {
    label: "Telemetry",
    href: "/telemetry/health",
    icon: Activity,
    available: true,
  },
  { label: "Operations", href: "/operations/apis", icon: Braces, available: true },
  { label: "Storage", href: "/storage/objects", icon: HardDrive, available: true },
];

export function ConsoleShell({
  activeRoute = "/",
  actor,
  children,
  csrfToken,
  headerDescription = "Runtime and capability status",
  headerTitle = "Platform overview",
}: {
  activeRoute?: string;
  actor: Actor;
  children: ReactNode;
  csrfToken: string;
  headerDescription?: string;
  headerTitle?: string;
}) {
  const { locale, t } = useLocale();

  useEffect(() => {
    document.title = `${t(headerTitle)} · Asterloom`;
  }, [headerTitle, locale, t]);

  return (
    <div className="min-h-dvh">
      <aside className="fixed inset-y-0 left-0 hidden w-64 border-r border-white/8 bg-slate-950/70 p-4 backdrop-blur-xl lg:flex lg:flex-col">
        <div className="flex h-14 items-center gap-3 px-2">
          <div className="grid size-9 place-items-center rounded-xl border border-sky-300/20 bg-sky-400/10 text-sky-300 shadow-lg shadow-sky-950">
            <RadioTower aria-hidden="true" className="size-4" />
          </div>
          <div>
            <p className="font-semibold tracking-tight text-white">Asterloom</p>
            <p className="text-[10px] uppercase tracking-[0.18em] text-slate-500">
              {t("Control plane")}
            </p>
          </div>
        </div>

        <nav aria-label={t("Primary navigation")} className="mt-8 space-y-1">
          {navigation.map((item) => {
            const Icon = item.icon;
            const content = (
              <>
                <Icon aria-hidden="true" className="size-4 shrink-0" />
                <span>{t(item.label)}</span>
                {!item.available && (
                  <span className="ml-auto text-[9px] uppercase tracking-wider text-slate-600">
                    {t("Planned")}
                  </span>
                )}
              </>
            );

            return item.available ? (
              <Link
                className={cn(
                  "flex h-10 items-center gap-3 rounded-lg px-3 text-sm font-medium transition-colors",
                  item.href === activeRoute
                    ? "bg-white/[0.07] text-white"
                    : "text-slate-400 hover:bg-white/[0.04] hover:text-slate-200",
                )}
                href={item.href}
                key={item.href}
              >
                {content}
              </Link>
            ) : (
              <span
                aria-disabled="true"
                className="flex h-10 cursor-not-allowed items-center gap-3 rounded-lg px-3 text-sm text-slate-600"
                key={item.href}
              >
                {content}
              </span>
            );
          })}
        </nav>

        <div className="mt-auto rounded-xl border border-white/8 bg-white/[0.025] p-3">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-300">{t("Control plane")}</span>
            <Badge variant="success">{t("Implemented")}</Badge>
          </div>
          <p className="mt-2 text-xs leading-5 text-slate-500">
            {t("All contract-first slices through Operations are live with complete Web management coverage.")}
          </p>
        </div>
      </aside>

      <div className={cn("min-h-dvh", "lg:pl-64")}>
        <header className="sticky top-0 z-30 flex h-17 items-center justify-between border-b border-white/8 bg-slate-950/65 px-5 backdrop-blur-xl sm:px-8">
          <div>
            <p className="text-xs font-medium uppercase tracking-[0.18em] text-sky-400">
              {t(headerTitle)}
            </p>
            <p className="mt-0.5 text-sm text-slate-500">
              {t(headerDescription)}
            </p>
          </div>
          <div className="flex items-center gap-2 sm:gap-3">
            <LocaleToggle />
            <ThemeToggle />
            <CommandMenu />
            <AccountMenu actor={actor} csrfToken={csrfToken} />
          </div>
        </header>

        <main className="mx-auto w-full max-w-7xl px-5 py-8 sm:px-8 sm:py-10">
          <div key={locale}>{children}</div>
        </main>
      </div>
    </div>
  );
}
