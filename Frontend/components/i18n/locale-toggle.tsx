"use client";

import { Languages } from "lucide-react";

import { useLocale } from "@/components/i18n/locale-provider";
import { Button } from "@/components/ui/button";

export function LocaleToggle() {
  const { locale, setLocale, t } = useLocale();
  const isEnglish = locale === "en";
  const label = isEnglish ? "English" : "Chinese";
  const nextLabel = isEnglish ? "Chinese" : "English";
  const description = t(`Language: ${label}. Switch to ${nextLabel}.`);

  return (
    <Button
      aria-label={description}
      className="px-2.5 sm:px-3"
      data-testid="locale-toggle"
      onClick={() => setLocale(isEnglish ? "zh-CN" : "en")}
      size="sm"
      title={description}
      type="button"
      variant="outline"
    >
      <Languages aria-hidden="true" className="size-3.5" />
      <span>{isEnglish ? "中文" : "EN"}</span>
    </Button>
  );
}
