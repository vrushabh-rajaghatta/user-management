import { describe, expect, it } from "vitest";
import configuration from "../vitest.config.ts";

/**
 * `npm test` must never need PostgreSQL, .NET, a certificate or a browser
 * (docs/frontend-architecture.md §16, AGENTS.md).
 *
 * The real-host tests live in host-tests/ under their own configuration. This
 * proves the everyday suite cannot pick them up: a glob widened to
 * "**\/*.test.ts" would otherwise quietly give the whole suite a dependency on
 * a running host, and the failure would look like a broken test rather than a
 * broken boundary.
 */

interface Project {
  readonly test?: { readonly name?: string; readonly include?: readonly string[] };
}

const projects = (configuration.test?.projects ?? []) as readonly Project[];

describe("the everyday test suite", () => {
  it("is made of projects that each name their own files", () => {
    expect(projects.length).toBeGreaterThan(0);

    for (const project of projects) {
      expect(project.test?.include ?? []).not.toHaveLength(0);
    }
  });

  it("looks only in src/ and tooling/, so nothing under host-tests can be collected", () => {
    const patterns = projects.flatMap((project) => [...(project.test?.include ?? [])]);

    expect(patterns.length).toBeGreaterThan(0);

    for (const pattern of patterns) {
      expect(pattern).not.toContain("host-tests");
      expect(pattern.startsWith("src/") || pattern.startsWith("tooling/")).toBe(true);
    }
  });
});
