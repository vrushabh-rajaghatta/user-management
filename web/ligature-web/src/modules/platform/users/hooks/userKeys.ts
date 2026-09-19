/**
 * This module's query keys, in one factory (docs/frontend-architecture.md §11).
 * Invalidation goes through here, never through a hand-typed array.
 */
export const userKeys = {
  all: ["users"] as const,
  /** Every page of the list. */
  lists: ["users", "list"] as const,
  list: (params: { page: number }) => ["users", "list", params] as const,

  /** Every read of one user's assignments, current and history alike. */
  roleAssignmentsOf: (userId: string) => ["users", "role-assignments", userId] as const,
  roleAssignments: (userId: string, params: { includeInactive: boolean }) =>
    ["users", "role-assignments", userId, params] as const,

  grantableRoles: ["users", "grantable-roles"] as const,

  /** IDN-Q1: one user's identities, with their lock state as of the read. */
  identities: (userId: string) => ["users", "identities", userId] as const,

  /** SES-Q1: one user's active sessions, as the server judged them at the read. */
  sessions: (userId: string) => ["users", "sessions", userId] as const,

  /** USR-Q1 GetUser v1: one user's profile names. */
  profile: (userId: string) => ["users", "profile", userId] as const,
};
