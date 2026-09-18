/**
 * This module's query keys, in one factory (docs/frontend-architecture.md §11).
 * Invalidation goes through here, never through a hand-typed array.
 */
export const userKeys = {
  all: ["users"] as const,
  list: (params: { page: number }) => ["users", "list", params] as const,

  /** Every read of one user's assignments, current and history alike. */
  roleAssignmentsOf: (userId: string) => ["users", "role-assignments", userId] as const,
  roleAssignments: (userId: string, params: { includeInactive: boolean }) =>
    ["users", "role-assignments", userId, params] as const,

  grantableRoles: ["users", "grantable-roles"] as const,
};
