import { defineConfig } from "vitest/config";

/**
 * The real-host transport tests (docs/frontend-architecture.md §16).
 *
 * A SEPARATE configuration, not a third project in vitest.config.ts, so that
 * `npm test` cannot grow a dependency on PostgreSQL, .NET, certificates or a
 * browser by accident. tooling/test-isolation.test.ts proves the separation.
 *
 * One file at a time: every test in here shares one host, one database and one
 * browser, and they sign in and out of the same account.
 */
export default defineConfig({
  test: {
    name: "host",
    environment: "node",
    include: ["host-tests/**/*.test.ts"],
    globalSetup: ["./host-tests/globalSetup.ts"],
    fileParallelism: false,
    testTimeout: 60_000,
    hookTimeout: 300_000,
  },
});
