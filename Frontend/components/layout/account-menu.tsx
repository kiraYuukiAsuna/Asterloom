"use client";

import { LogOut, UserRound } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import type { Actor } from "@/lib/auth/types";

export function AccountMenu({
  actor,
  csrfToken,
}: {
  actor: Actor;
  csrfToken: string;
}) {
  const [pending, setPending] = useState(false);

  async function logout() {
    setPending(true);
    try {
      const response = await fetch("/api/auth/logout", {
        headers: { "x-csrf-token": csrfToken },
        method: "POST",
      });
      if (!response.ok) {
        throw new Error("Logout failed");
      }
      const payload = (await response.json()) as { logoutUrl: string };
      window.location.assign(payload.logoutUrl);
    } catch {
      toast.error("Unable to sign out. Please try again.");
      setPending(false);
    }
  }

  return (
    <div className="flex items-center gap-2">
      <div className="hidden text-right sm:block">
        <p className="max-w-48 truncate text-xs font-medium text-slate-200">
          {actor.name}
        </p>
        <p className="max-w-48 truncate text-[10px] text-slate-500">
          {actor.email ?? actor.subject}
        </p>
      </div>
      <div className="grid size-9 place-items-center rounded-xl border border-white/8 bg-white/[0.04] text-slate-400">
        <UserRound aria-hidden="true" className="size-4" />
      </div>
      <Button
        aria-label="Sign out"
        disabled={pending}
        onClick={logout}
        size="icon"
        title="Sign out"
        type="button"
        variant="ghost"
      >
        <LogOut aria-hidden="true" className="size-4" />
      </Button>
    </div>
  );
}
