import type { RequestHandler } from "msw";

/**
 * Handlers every test gets by default. Deliberately empty: a response a test
 * relies on is declared in that test, where a reader can see it.
 */
export const handlers: RequestHandler[] = [];
