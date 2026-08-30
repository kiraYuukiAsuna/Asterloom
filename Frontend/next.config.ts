import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output:
    process.env.ASTERLOOM_NEXT_STANDALONE === "false" ? undefined : "standalone",
  reactStrictMode: true,
  poweredByHeader: false,
  experimental: {
    extensionAlias: {
      ".js": [".ts", ".tsx", ".js"],
      ".jsx": [".tsx", ".jsx"],
    },
  },
};

export default nextConfig;
