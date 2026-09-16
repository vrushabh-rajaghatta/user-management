import path from "node:path";
import js from "@eslint/js";
import boundaries from "eslint-plugin-boundaries";
import jsxA11y from "eslint-plugin-jsx-a11y";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import { defineConfig, globalIgnores } from "eslint/config";
import globals from "globals";
import tseslint from "typescript-eslint";

const root = import.meta.dirname;

/**
 * Deliberately broken code that proves the architecture rules below still
 * fire (tooling/architecture-lint.test.ts). Ignored by `npm run lint`, and
 * linted with this same configuration by that test.
 */
const LINT_FIXTURES = "tooling/lint-fixtures";

/** Application source, and the fixture project that mirrors its layout. */
const SOURCE = ["src/**/*.{ts,tsx}", `${LINT_FIXTURES}/project/src/**/*.{ts,tsx}`];

/** Tests and their infrastructure: free to reach into what they test. */
const TESTS = ["**/*.test.{ts,tsx}", "**/src/test/**"];

// ---------------------------------------------------------------- §6 and §8

const NETWORK_MESSAGE =
  "Only src/shared/api/client.ts talks to the network (docs/frontend-architecture.md §6). "
  + "Call the module's API operation through its hook.";

const STORAGE_MESSAGE =
  "Web storage is reserved for the session hint source (docs/frontend-architecture.md §8). "
  + "It is not a place for application state, and never for a credential.";

const NETWORK_GLOBALS = ["fetch", "XMLHttpRequest"];

const STORAGE_GLOBALS = ["sessionStorage", "localStorage"];

/** A global and the same name reached through window, globalThis or self. */
function restricted(names, message) {
  return {
    "no-restricted-globals": ["error", ...names.map((name) => ({ name, message }))],
    "no-restricted-properties": [
      "error",
      ...names.flatMap((property) =>
        ["window", "globalThis", "self"].map((object) => ({ object, property, message })),
      ),
    ],
  };
}

function restrictedEverything() {
  const network = restricted(NETWORK_GLOBALS, NETWORK_MESSAGE);
  const storage = restricted(STORAGE_GLOBALS, STORAGE_MESSAGE);

  return {
    "no-restricted-globals": [...network["no-restricted-globals"], ...storage["no-restricted-globals"].slice(1)],
    "no-restricted-properties": [...network["no-restricted-properties"], ...storage["no-restricted-properties"].slice(1)],
  };
}

// ---------------------------------------------------------------------- §2

/**
 * The layers of docs/frontend-architecture.md §2. Matched against the end of a
 * file's path, so the fixture project classifies exactly as src/ does.
 *
 * A module's api/ and hooks/ folders are elements of their own. The plugin
 * checks dependencies BETWEEN elements only, so if a module were one element,
 * a component importing its own module's API operation would never be
 * examined. The first matching pattern wins, which is why they come before
 * the module itself.
 */
const ELEMENTS = [
  { type: "app", pattern: "src/app" },
  { type: "shared", pattern: "src/shared/*", capture: ["area"] },
  { type: "ui", pattern: "src/components/ui" },
  { type: "lib", pattern: "src/lib" },
  { type: "module-api", pattern: "src/modules/*/*/api", capture: ["family", "module"] },
  { type: "module-hooks", pattern: "src/modules/*/*/hooks", capture: ["family", "module"] },
  { type: "module", pattern: "src/modules/*/*", capture: ["family", "module"] },
  { type: "test", pattern: "src/test" },
];

const MODULE_TYPES = ["module", "module-hooks", "module-api"];

const ANY_MODULE = { types: { anyOf: MODULE_TYPES } };

const SAME_MODULE = {
  family: "{{ from.element.captured.family }}",
  module: "{{ from.element.captured.module }}",
};

const API_CLIENT = { type: "shared", captured: { area: "api" }, fileInternalPath: "client.ts" };

/**
 * Anything not allowed here is refused (default: "disallow"). Later policies
 * override earlier ones, so each refusal follows the allowances it narrows.
 */
const POLICIES = [
  // Packages and Node built-ins are not layers of this application.
  { allow: { to: { module: { origin: "external" } } } },
  { allow: { to: { module: { origin: "core" } } } },

  // app/ composes: shared infrastructure, and each module's public surface and routes only.
  { from: { element: { type: "app" } }, allow: { to: { element: { types: { anyOf: ["app", "shared", "ui", "lib"] } } } } },
  { from: { element: { type: "app" } }, allow: { to: { element: { type: "module", fileInternalPath: "index.ts" } } } },
  { from: { element: { type: "app" } }, allow: { to: { element: { type: "module", fileInternalPath: "routes.tsx" } } } },

  // shared/ is infrastructure: it never imports a module or app/.
  { from: { element: { type: "shared" } }, allow: { to: { element: { types: { anyOf: ["shared", "ui", "lib"] } } } } },

  // components/ui are vendored primitives; lib/ is pure helpers.
  { from: { element: { type: "ui" } }, allow: { to: { element: { types: { anyOf: ["ui", "lib"] } } } } },
  { from: { element: { type: "lib" } }, allow: { to: { element: { type: "lib" } } } },

  // Every part of a module may use infrastructure, and another module's public surface.
  { from: { element: ANY_MODULE }, allow: { to: { element: { types: { anyOf: ["shared", "ui", "lib"] } } } } },
  { from: { element: ANY_MODULE }, allow: { to: { element: { type: "module", fileInternalPath: "index.ts" } } } },

  // Within one module: components, pages, schemas and the public surface use
  // the module's own files and hooks — never its API operations.
  { from: { element: { type: "module" } }, allow: { to: { element: { types: { anyOf: ["module", "module-hooks"] }, captured: SAME_MODULE } } } },

  // §6: hooks are the only callers of their module's API operations.
  { from: { element: { type: "module-hooks" } }, allow: { to: { element: { ...ANY_MODULE, captured: SAME_MODULE } } } },

  // API operations use their module's schemas and files, never its hooks.
  { from: { element: { type: "module-api" } }, allow: { to: { element: { type: "module", captured: SAME_MODULE } } } },

  // A platform module may not use a business module (docs/architecture.md §10).
  {
    from: { element: { ...ANY_MODULE, captured: { family: "platform" } } },
    disallow: { to: { element: { ...ANY_MODULE, captured: { family: "!platform" } } } },
  },

  // §6: the API client is imported only by module API operations and shared/auth.
  { disallow: { to: { element: API_CLIENT } } },
  { from: { element: { type: "module-api" } }, allow: { to: { element: API_CLIENT } } },
  { from: { element: { type: "shared", captured: { area: "auth" } } }, allow: { to: { element: API_CLIENT } } },
];

export default defineConfig([
  globalIgnores(["dist", "coverage", LINT_FIXTURES]),

  {
    files: ["**/*.{ts,tsx}"],
    extends: [
      js.configs.recommended,
      tseslint.configs.strictTypeChecked,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      jsxA11y.flatConfigs.recommended,
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: root,
      },
    },
  },

  // The development environment itself runs in Node.
  {
    files: ["vite.config.ts", "vitest.config.ts", "vitest.host.config.ts", "tooling/**/*.ts", "host-tests/**/*.ts"],
    languageOptions: { globals: globals.node },
  },

  // This file is JavaScript and belongs to no TypeScript project.
  {
    files: ["eslint.config.js"],
    extends: [js.configs.recommended, tseslint.configs.disableTypeChecked],
    languageOptions: { globals: globals.node },
  },

  // Vendored shadcn primitives export a variants constant beside the component
  // by design. They are re-installed, never hand-edited, so the rule is scoped
  // off here rather than the files being restructured away from upstream.
  {
    files: ["src/components/ui/**/*.{ts,tsx}"],
    rules: {
      "react-refresh/only-export-components": "off",

      // The vendored Label is a generic wrapper: it cannot associate itself
      // with a control, because the control is the caller's. FormField is where
      // the association is made and where its tests prove it. These files are
      // re-installed rather than hand-edited, so the rule is scoped off here
      // instead of the primitive being patched away from upstream.
      "jsx-a11y/label-has-associated-control": "off",
    },
  },

  // §2 and §6: layer boundaries. Tests and the entry point are exempt: tests
  // reach into what they test, and main.tsx only mounts the composition root.
  {
    files: SOURCE,
    ignores: [...TESTS, "src/main.tsx"],
    plugins: { boundaries },
    settings: {
      "boundaries/elements": ELEMENTS,
      "import/resolver": {
        typescript: {
          alwaysTryTypes: true,
          // Two projects on purpose: the application, and the fixture project
          // that proves these rules. Each resolves its own "@/" alias.
          project: [path.join(root, "tsconfig.json"), path.join(root, LINT_FIXTURES, "project", "tsconfig.json")],
          noWarnOnMultipleProjects: true,
        },
      },
    },
    rules: {
      "boundaries/dependencies": ["error", { default: "disallow", policies: POLICIES }],
    },
  },

  // §9: no code inspects permissions except shared/auth, and React Router 8
  // has no react-router-dom package to import from.
  {
    files: SOURCE,
    ignores: TESTS,
    rules: {
      "no-restricted-imports": [
        "error",
        {
          paths: [
            {
              name: "react-router-dom",
              message: "React Router 8 has no react-router-dom. Import from \"react-router\", and RouterProvider from \"react-router/dom\".",
            },
          ],
        },
      ],
      "no-restricted-syntax": [
        "error",
        {
          selector: "MemberExpression[property.name='permissions']",
          message:
            "Only shared/auth reads effective permissions (docs/frontend-architecture.md §9). Use can(), useCan() or <Can> — and remember they decide visibility, never authorization.",
        },
        {
          selector: "MemberExpression[property.name='returnTo']",
          message:
            "Only shared/auth reads the return path (docs/frontend-architecture.md §13). Use useReturnPath(), which validates it in the same place it is read: an unvalidated return path is an open redirect.",
        },
      ],
    },
  },
  { files: ["**/src/shared/auth/**/*.{ts,tsx}"], rules: { "no-restricted-syntax": "off" } },

  // §12: the form presentation primitives are handed an error string and know
  // nothing of where it came from. Restated in full rather than extended,
  // because re-declaring the rule replaces its options.
  {
    files: ["**/src/shared/forms/**/*.{ts,tsx}"],
    ignores: TESTS,
    rules: {
      "no-restricted-imports": [
        "error",
        {
          paths: [
            {
              name: "react-router-dom",
              message: "React Router 8 has no react-router-dom. Import from \"react-router\", and RouterProvider from \"react-router/dom\".",
            },
            {
              name: "zod",
              message: "FormField knows nothing of schemas (docs/frontend-architecture.md §12). It is handed an error string; the caller decides what produced it.",
            },
            {
              name: "react-hook-form",
              message: "No form library in W3 (docs/frontend-architecture.md §12), and FormField owns accessible structure rather than form state.",
            },
          ],
        },
      ],
    },
  },

  // §6 and §8: the network and web storage, each with its one permitted home.
  { files: SOURCE, rules: restrictedEverything() },
  { files: ["**/src/shared/api/client.ts"], rules: restricted(STORAGE_GLOBALS, STORAGE_MESSAGE) },
  { files: ["**/src/shared/auth/SessionHintSource.ts"], rules: restricted(NETWORK_GLOBALS, NETWORK_MESSAGE) },
  { files: TESTS, rules: { "no-restricted-globals": "off", "no-restricted-properties": "off" } },
]);
