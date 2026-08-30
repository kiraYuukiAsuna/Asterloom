"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useSyncExternalStore,
  type ReactNode,
} from "react";
import { Toaster } from "sonner";

import {
  normalizeThemePreference,
  resolveTheme,
  themeStorageKey,
  type ResolvedTheme,
  type ThemePreference,
} from "@/lib/ui/theme";

type ThemeContextValue = {
  resolvedTheme: ResolvedTheme;
  setTheme: (theme: ThemePreference) => void;
  theme: ThemePreference;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);
const themeChangeEvent = "asterloom-theme-change";

function applyTheme(preference: ThemePreference): ResolvedTheme {
  const resolved = resolveTheme(
    preference,
    window.matchMedia("(prefers-color-scheme: dark)").matches,
  );
  const root = document.documentElement;
  root.classList.remove("light", "dark");
  root.classList.add(resolved);
  root.dataset.theme = resolved;
  root.dataset.themePreference = preference;
  return resolved;
}

function readThemePreference(): ThemePreference {
  try {
    return normalizeThemePreference(
      window.localStorage.getItem(themeStorageKey),
    );
  } catch {
    return normalizeThemePreference(
      document.documentElement.dataset.themePreference,
    );
  }
}

function getThemeSnapshot(): string {
  const preference = readThemePreference();
  const resolved = resolveTheme(
    preference,
    window.matchMedia("(prefers-color-scheme: dark)").matches,
  );
  return `${preference}:${resolved}`;
}

function subscribeToTheme(onStoreChange: () => void): () => void {
  const media = window.matchMedia("(prefers-color-scheme: dark)");
  const synchronize = () => {
    applyTheme(readThemePreference());
    onStoreChange();
  };
  const handleStorage = (event: StorageEvent) => {
    if (event.key === themeStorageKey) {
      synchronize();
    }
  };

  media.addEventListener("change", synchronize);
  window.addEventListener(themeChangeEvent, synchronize);
  window.addEventListener("storage", handleStorage);
  return () => {
    media.removeEventListener("change", synchronize);
    window.removeEventListener(themeChangeEvent, synchronize);
    window.removeEventListener("storage", handleStorage);
  };
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const snapshot = useSyncExternalStore(
    subscribeToTheme,
    getThemeSnapshot,
    () => "system:dark",
  );
  const [theme, resolvedTheme] = snapshot.split(":") as [
    ThemePreference,
    ResolvedTheme,
  ];

  const setTheme = useCallback((nextTheme: ThemePreference) => {
    try {
      window.localStorage.setItem(themeStorageKey, nextTheme);
    } catch {
      // The active theme still works when storage is unavailable.
    }
    applyTheme(nextTheme);
    window.dispatchEvent(new Event(themeChangeEvent));
  }, []);

  const value = useMemo(
    () => ({ resolvedTheme, setTheme, theme }),
    [resolvedTheme, setTheme, theme],
  );

  return (
    <ThemeContext.Provider value={value}>
      {children}
      <Toaster
        closeButton
        position="bottom-right"
        richColors
        theme={resolvedTheme}
        toastOptions={{ className: "asterloom-toast" }}
      />
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used inside ThemeProvider.");
  }
  return context;
}
