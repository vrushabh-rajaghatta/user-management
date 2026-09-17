/**
 * This module's query keys, in one factory (docs/frontend-architecture.md §11).
 * Invalidation goes through here, never through a hand-typed array.
 */
export const userKeys = {
  all: ["users"] as const,
  list: (params: { page: number }) => ["users", "list", params] as const,
};
