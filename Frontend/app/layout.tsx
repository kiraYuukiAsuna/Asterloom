import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { LocaleProvider } from "@/components/i18n/locale-provider";
import { ThemeProvider } from "@/components/theme/theme-provider";
import { localeStorageKey } from "@/lib/i18n/locale";
import { themeStorageKey } from "@/lib/ui/theme";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: {
    default: "Asterloom Console",
    template: "%s · Asterloom",
  },
  description: "Unified control plane for Asterloom platform capabilities.",
};

const themeInitializer = `(() => { try { const stored = localStorage.getItem(${JSON.stringify(themeStorageKey)}); const preference = stored === "light" || stored === "dark" || stored === "system" ? stored : "system"; const resolved = preference === "system" ? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light") : preference; const root = document.documentElement; root.classList.remove("light", "dark"); root.classList.add(resolved); root.dataset.theme = resolved; root.dataset.themePreference = preference; } catch { document.documentElement.classList.add("dark"); } })();`;
const localeInitializer = `(() => { try { const stored = localStorage.getItem(${JSON.stringify(localeStorageKey)}); const browser = navigator.languages?.[0] ?? navigator.language; const requested = stored || browser || "en"; const locale = requested.toLowerCase() === "zh" || requested.toLowerCase().startsWith("zh-") ? "zh-CN" : "en"; const root = document.documentElement; root.lang = locale; root.dir = "ltr"; root.dataset.locale = locale; } catch { document.documentElement.lang = "en"; document.documentElement.dataset.locale = "en"; } })();`;

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: localeInitializer }} />
        <script dangerouslySetInnerHTML={{ __html: themeInitializer }} />
      </head>
      <body className={geistSans.variable + " " + geistMono.variable}>
        <LocaleProvider>
          <ThemeProvider>{children}</ThemeProvider>
        </LocaleProvider>
      </body>
    </html>
  );
}
