"use client";

import { Monitor, Moon, Sun } from "lucide-react";

import { useLocale } from "@/components/i18n/locale-provider";
import { Button } from "@/components/ui/button";
import { useTheme } from "@/components/theme/theme-provider";
import {
  nextThemePreference,
  type ThemePreference,
} from "@/lib/ui/theme";

const themeDetails = {
  system: { icon: Monitor, label: "System" },
  light: { icon: Sun, label: "Light" },
  dark: { icon: Moon, label: "Dark" },
} satisfies Record<ThemePreference, { icon: typeof Sun; label: string }>;

export function ThemeToggle() {
  const { t } = useLocale();
  const { setTheme, theme } = useTheme();
  const nextTheme = nextThemePreference(theme);
  const { icon: Icon, label } = themeDetails[theme];

  return (
    <Button
      aria-label={t(`Color theme: ${label}. Switch to ${themeDetails[nextTheme].label}.`)}
      className="px-2.5 sm:px-3"
      data-testid="theme-toggle"
      onClick={() => setTheme(nextTheme)}
      size="sm"
      title={t(`Theme: ${label}. Click for ${themeDetails[nextTheme].label}.`)}
      type="button"
      variant="outline"
    >
      <Icon aria-hidden="true" className="size-3.5" />
      <span className="hidden xl:inline">{t(label)}</span>
    </Button>
  );
}
