import { definePermission } from "@/shared/auth/permissions";

/**
 * This module's permission codes, verbatim from the backend catalogue
 * (docs/frontend-architecture.md §9).
 *
 * role.read is OWNED HERE (RA8): it inspects role definitions. The users module
 * consumes it through this module's public surface for its Manage roles action.
 * role.manage, which CHANGES a definition, belongs to AUT-C3-C8 and is not used
 * anywhere yet (RA9).
 */
export const RolePermissions = {
  read: definePermission("role.read"),
};
