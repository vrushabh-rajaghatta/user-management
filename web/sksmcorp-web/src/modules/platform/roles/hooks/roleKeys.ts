/**
 * This module's query keys, in one factory (docs/frontend-architecture.md §11).
 */
export const roleKeys = {
  all: ["roles"] as const,
  list: (params: { includeInactive: boolean }) => ["roles", "list", params] as const,
  /** The release-owned permission catalogue (AUT-Q6), which AUT-C7 picks from. */
  catalogue: ["roles", "catalogue"] as const,

  /** One role's grants. Named for what they are: role grants, not a caller's effective permissions. */
  grants: (roleId: string) => ["roles", "grants", roleId] as const,

  /** One role's holders (AUT-Q4), with the count derived from those same rows. */
  members: (roleId: string) => ["roles", "members", roleId] as const,
};
