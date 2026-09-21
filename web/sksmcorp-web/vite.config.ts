import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { apiProxy, devServerHttps, resolveApiOrigin, resolveDevServerHost } from "./tooling/dev-server.ts";

// https://vite.dev/config/
export default defineConfig(({ command, isPreview }) => ({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "src"),
    },
  },

  // The development server only: HTTPS with a locally trusted certificate,
  // and a same-origin proxy for /api (tooling/dev-server.ts explains both).
  // `vite build` needs no certificate, and the test runner reads
  // vitest.config.ts instead of this file.
  server:
    command === "serve" && isPreview !== true
      ? {
          host: resolveDevServerHost(process.env),
          port: 5173,
          strictPort: true,
          https: devServerHttps(path.resolve(import.meta.dirname, ".certs")),
          proxy: apiProxy(resolveApiOrigin(process.env)),
        }
      : {},
}));
