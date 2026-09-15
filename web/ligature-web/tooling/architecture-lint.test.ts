import { readdirSync } from "node:fs";
import path from "node:path";
import { ESLint, type Linter } from "eslint";
import { beforeAll, describe, expect, it } from "vitest";

/**
 * The architecture rules of docs/frontend-architecture.md §2, §6 and §8, proved
 * rather than asserted.
 *
 * The fixture project mirrors the application's layout. It is linted with the
 * real eslint.config.js — not a copy of its rules — so a rule that is switched
 * off, loosened or misconfigured there fails here, instead of `npm run lint`
 * passing because it no longer checks anything.
 *
 * Every fixture is listed below as a violation of exactly one rule or as an
 * allowed control. The controls matter as much as the violations: a rule that
 * fired on everything would satisfy the violations alone.
 */

const APP_ROOT = path.resolve(import.meta.dirname, "..");

const FIXTURE_SOURCE = path.join(import.meta.dirname, "lint-fixtures", "project", "src");

const ARCHITECTURE_RULES = new Set(["boundaries/dependencies", "no-restricted-globals", "no-restricted-properties"]);

const VIOLATIONS: [file: string, rule: string, reason: string][] = [
  ["shared/components/LeakyBadge.ts", "boundaries/dependencies", "shared/ imports a module"],
  ["components/ui/usesShared.ts", "boundaries/dependencies", "a vendored primitive imports shared/"],
  ["app/reachesIntoPages.ts", "boundaries/dependencies", "app/ imports a module internal"],
  ["modules/platform/users/hooks/usesClientDirectly.ts", "boundaries/dependencies", "a hook imports the API client"],
  ["modules/platform/users/components/CreateUserForm.ts", "boundaries/dependencies", "a component imports its own module's API operation"],
  ["modules/platform/users/api/usesHooks.ts", "boundaries/dependencies", "an API operation imports its module's hooks"],
  ["modules/platform/roles/hooks/reachesIntoUsersHooks.ts", "boundaries/dependencies", "a module imports another module's internals by alias"],
  ["modules/platform/roles/hooks/reachesIntoUsersApi.ts", "boundaries/dependencies", "a module imports another module's API operation by relative path"],
  ["modules/platform/roles/hooks/usesBusinessModule.ts", "boundaries/dependencies", "a platform module imports a business module"],
  ["modules/platform/users/hooks/fetchesDirectly.ts", "no-restricted-globals", "fetch outside the API client"],
  ["modules/platform/users/hooks/fetchesThroughWindow.ts", "no-restricted-properties", "window.fetch outside the API client"],
  ["modules/platform/users/hooks/usesXmlHttpRequest.ts", "no-restricted-globals", "XMLHttpRequest outside the API client"],
  ["modules/platform/users/hooks/usesLocalStorage.ts", "no-restricted-globals", "localStorage outside the hint source"],
  ["modules/platform/users/hooks/usesSessionStorageThroughGlobalThis.ts", "no-restricted-properties", "globalThis.sessionStorage outside the hint source"],
];

const CONTROLS: [file: string, reason: string][] = [
  ["shared/api/client.ts", "the API client may use the network"],
  ["shared/auth/SessionHintSource.ts", "shared/auth may import the API client, and the hint source may use web storage"],
  ["lib/utils.ts", "a pure helper"],
  ["components/ui/button.ts", "a vendored primitive may use lib/"],
  ["app/router.ts", "app/ may use a module's routes and public surface"],
  ["modules/platform/users/index.ts", "a module's public surface may use its own hooks"],
  ["modules/platform/users/routes.tsx", "a module's routes"],
  ["modules/platform/users/pages/CreateUserPage.ts", "a page"],
  ["modules/platform/users/api/createUser.ts", "an API operation may import the API client and its module's schema"],
  ["modules/platform/users/schemas/createUser.ts", "a module's schema"],
  ["modules/platform/users/hooks/useCreateUser.ts", "a hook may import its own module's API operation"],
  ["modules/platform/roles/index.ts", "a module's public surface"],
  ["modules/platform/roles/hooks/usesUsersPublicSurface.ts", "a module may use another module's public surface"],
  ["modules/regulatory/submissions/index.ts", "a business module may use a platform module's public surface"],
];

let results: Map<string, Linter.LintMessage[]>;

beforeAll(async () => {
  const eslint = new ESLint({ cwd: APP_ROOT, ignore: false });

  const linted = await eslint.lintFiles([path.join(FIXTURE_SOURCE, "**/*.{ts,tsx}")]);

  results = new Map(linted.map((result) => [path.relative(FIXTURE_SOURCE, result.filePath), result.messages]));
}, 120_000);

describe("the architecture lint rules", () => {
  it("lint every fixture without a parse failure, so none can pass by not being checked", () => {
    const fatal = [...results].filter(([, messages]) => messages.some((message) => message.fatal === true));

    expect(fatal).toEqual([]);
    expect(results.size).toBe(listFixtures().length);
  });

  it("have an expectation for every fixture", () => {
    const expected = [...VIOLATIONS.map(([file]) => file), ...CONTROLS.map(([file]) => file)].sort();

    expect(listFixtures()).toEqual(expected);
  });

  it.each(VIOLATIONS)("report %s under %s: %s", (file, rule) => {
    expect(architectureRules(file)).toEqual([rule]);
  });

  it.each(CONTROLS)("allow %s: %s", (file) => {
    expect(architectureRules(file)).toEqual([]);
  });
});

/** The distinct architecture rules reported for a fixture. */
function architectureRules(file: string): string[] {
  const messages = results.get(file);

  if (messages === undefined) {
    throw new Error(`${file} was not linted.`);
  }

  const rules = messages
    .map((message) => message.ruleId)
    .filter((rule): rule is string => rule !== null && ARCHITECTURE_RULES.has(rule));

  return [...new Set(rules)];
}

function listFixtures(): string[] {
  return readdirSync(FIXTURE_SOURCE, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.relative(FIXTURE_SOURCE, path.join(entry.parentPath, entry.name)))
    .sort();
}
