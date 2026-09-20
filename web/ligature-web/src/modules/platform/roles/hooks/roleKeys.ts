/**
 * This module's query keys, in one factory (docs/frontend-architecture.md §11).
 */
export const roleKeys = {
  all: ["roles"] as const,
  list: (params: { includeInactive: boolean }) => ["roles", "list", params] as const,
  /** One role's grants. Named for what they are: role grants, not a caller's effective permissions. */
  grants: (roleId: string) => ["roles", "grants", roleId] as const,
};
