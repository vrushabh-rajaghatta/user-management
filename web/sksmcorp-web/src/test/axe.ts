import axe from "axe-core";
import { expect } from "vitest";

/**
 * Rendered accessibility semantics (docs/frontend-architecture.md §15).
 *
 * Colour contrast is switched off, and deliberately so: jsdom does not lay out a
 * page, so axe cannot compute the colours text is actually drawn in. A passing
 * run here makes no claim about contrast. That is proved separately, and
 * deterministically, by tooling/theme-contrast.test.ts against the theme tokens.
 */
export async function expectNoAccessibilityViolations(container: Element): Promise<void> {
  const results = await axe.run(container, {
    rules: { "color-contrast": { enabled: false } },
  });

  const violations = results.violations.map(
    (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(" ")).join(", ")}`,
  );

  expect(violations).toEqual([]);
}
