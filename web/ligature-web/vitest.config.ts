import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

/**
 * Two projects, because they need different worlds.
 *
 * "web" is the application: jsdom, Testing Library, and MSW refusing any
 * request a test has not declared a handler for. It never needs PostgreSQL or
 * .NET.
 *
 * "tooling" is the development environment itself — the dev server's proxy
 * and certificate handling, and the lint rules that guard the architecture. It
 * runs in Node and makes real local connections, which MSW would refuse.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "src"),
    },
  },
  test: {
    projects: [
      {
        extends: true,
        test: {
          name: "web",
          environment: "jsdom",
          include: ["src/**/*.test.{ts,tsx}"],
          setupFiles: ["./src/test/setup.ts"],
        },
      },
      {
        extends: true,
        test: {
          name: "tooling",
          environment: "node",
          include: ["tooling/**/*.test.ts"],
        },
      },
    ],
  },
});
