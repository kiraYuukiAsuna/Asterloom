import { cva, type VariantProps } from "class-variance-authority";
import * as React from "react";

import { cn } from "@/lib/utils/cn";

const badgeVariants = cva(
  "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em]",
  {
    variants: {
      variant: {
        success: "border-emerald-400/20 bg-emerald-400/10 text-emerald-300",
        planned: "border-slate-400/15 bg-slate-400/5 text-slate-400",
        info: "border-sky-400/20 bg-sky-400/10 text-sky-300",
      },
    },
    defaultVariants: {
      variant: "info",
    },
  },
);

export interface BadgeProps
  extends React.ComponentProps<"span">,
    VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return (
    <span className={cn(badgeVariants({ variant }), className)} {...props} />
  );
}
