import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Toaster } from "sonner";

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

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html className="dark" lang="en">
      <body className={geistSans.variable + " " + geistMono.variable}>
        {children}
        <Toaster
          closeButton
          position="bottom-right"
          richColors
          theme="dark"
          toastOptions={{
            className: "border-white/10 bg-slate-950 text-slate-100",
          }}
        />
      </body>
    </html>
  );
}
