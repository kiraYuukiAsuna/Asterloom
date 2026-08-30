"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useSyncExternalStore,
  type ReactNode,
} from "react";

import {
  localeCookieName,
  localeStorageKey,
  normalizeLocale,
  setActiveLocale,
  translateForLocale,
  type Locale,
  type TranslationValues,
} from "@/lib/i18n/locale";

type LocaleContextValue = {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (message: string, values?: TranslationValues) => string;
};

const LocaleContext = createContext<LocaleContextValue | null>(null);
const localeChangeEvent = "asterloom-locale-change";

function applyLocale(locale: Locale): void {
  setActiveLocale(locale);
  const root = document.documentElement;
  root.lang = locale;
  root.dir = "ltr";
  root.dataset.locale = locale;
}

function readLocale(): Locale {
  try {
    const stored = window.localStorage.getItem(localeStorageKey);
    if (stored) return normalizeLocale(stored);
  } catch {
    // Browser preference and the initializer remain available.
  }
  const initialized = document.documentElement.dataset.locale;
  return initialized
    ? normalizeLocale(initialized)
    : normalizeLocale(window.navigator.languages?.[0] ?? window.navigator.language);
}

function getLocaleSnapshot(): Locale {
  const locale = readLocale();
  setActiveLocale(locale);
  return locale;
}

function subscribeToLocale(onStoreChange: () => void): () => void {
  const synchronize = () => {
    applyLocale(readLocale());
    onStoreChange();
  };
  const handleStorage = (event: StorageEvent) => {
    if (event.key === localeStorageKey) synchronize();
  };

  window.addEventListener(localeChangeEvent, synchronize);
  window.addEventListener("storage", handleStorage);
  return () => {
    window.removeEventListener(localeChangeEvent, synchronize);
    window.removeEventListener("storage", handleStorage);
  };
}

export function LocaleProvider({ children }: { children: ReactNode }) {
  const locale = useSyncExternalStore(
    subscribeToLocale,
    getLocaleSnapshot,
    () => "en" as const,
  );

  const setLocale = useCallback((nextLocale: Locale) => {
    try {
      window.localStorage.setItem(localeStorageKey, nextLocale);
    } catch {
      // The active locale still works when storage is unavailable.
    }
    document.cookie = `${localeCookieName}=${encodeURIComponent(nextLocale)}; Path=/; Max-Age=31536000; SameSite=Lax`;
    applyLocale(nextLocale);
    window.dispatchEvent(new Event(localeChangeEvent));
  }, []);

  const value = useMemo<LocaleContextValue>(
    () => ({
      locale,
      setLocale,
      t: (message, values) => translateForLocale(locale, message, values),
    }),
    [locale, setLocale],
  );

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleContextValue {
  const context = useContext(LocaleContext);
  if (!context) throw new Error("useLocale must be used inside LocaleProvider.");
  return context;
}
