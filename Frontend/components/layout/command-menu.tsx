"use client";

import { Command } from "cmdk";
import {
  Activity,
  Boxes,
  Braces,
  Building2,
  ChartNoAxesCombined,
  Flag,
  HardDrive,
  LayoutDashboard,
  Mail,
  PackageOpen,
  Search,
  ScrollText,
  ShieldCheck,
  SlidersHorizontal,
  Users,
} from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { Button } from "@/components/ui/button";
import { useLocale } from "@/components/i18n/locale-provider";

const destinations = [
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
  { label: "Mail", href: "/mail/accounts", icon: Mail, available: true },
  { label: "Storage", href: "/storage/objects", icon: HardDrive, available: true },
];

export function CommandMenu() {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const router = useRouter();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <>
      <Button
        aria-label={t("Open command menu")}
        onClick={() => setOpen(true)}
        size="sm"
        type="button"
        variant="outline"
      >
        <Search aria-hidden="true" className="size-3.5" />
        <span className="hidden sm:inline">{t("Jump to")}</span>
        <kbd className="hidden rounded border border-white/10 bg-black/20 px-1.5 py-0.5 font-mono text-[10px] text-slate-400 md:inline">
          Ctrl K
        </kbd>
      </Button>

      <Command.Dialog
        className="fixed left-1/2 top-[16vh] z-50 w-[min(92vw,36rem)] -translate-x-1/2 overflow-hidden rounded-2xl border border-white/10 bg-slate-950/95 shadow-2xl shadow-black/60 backdrop-blur-xl"
        label={t("Navigate Asterloom")}
        onOpenChange={setOpen}
        open={open}
      >
        <div className="flex items-center gap-3 border-b border-white/8 px-4">
          <Search aria-hidden="true" className="size-4 text-slate-500" />
          <Command.Input
            autoFocus
            className="h-13 w-full bg-transparent text-sm text-white outline-none placeholder:text-slate-600"
            placeholder={t("Search modules and resources…")}
          />
        </div>
        <Command.List className="max-h-80 overflow-y-auto p-2">
          <Command.Empty className="p-8 text-center text-sm text-slate-500">
            {t("No destination found.")}
          </Command.Empty>
          <Command.Group heading={t("Platform")}>
            {destinations.map((destination) => {
              const Icon = destination.icon;
              return (
                <Command.Item
                  className="flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2.5 text-sm text-slate-300 outline-none data-[selected=true]:bg-white/[0.07] data-[selected=true]:text-white data-[disabled=true]:cursor-not-allowed data-[disabled=true]:opacity-35"
                  disabled={!destination.available}
                  key={destination.href}
                  onSelect={() => {
                    router.push(destination.href);
                    setOpen(false);
                  }}
                  value={`${destination.label} ${t(destination.label)}`}
                >
                  <Icon aria-hidden="true" className="size-4" />
                  <span>{t(destination.label)}</span>
                  {!destination.available && (
                    <span className="ml-auto text-[10px] uppercase tracking-wider">
                      {t("Planned")}
                    </span>
                  )}
                </Command.Item>
              );
            })}
          </Command.Group>
        </Command.List>
      </Command.Dialog>
    </>
  );
}
