# Lint fixtures

A miniature of the web client layout, linted by `tooling/architecture-lint.test.ts`
with the real `eslint.config.js`. Each file either breaks exactly one architecture
rule on purpose, or is the allowed control beside it. The test names which is which;
a fixture it does not list fails the test.

Never imported by the application, and ignored by `npm run lint`.
