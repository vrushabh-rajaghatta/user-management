import { z } from "zod";

/**
 * AUT-Q5's response (docs/requirements.md, "Role administration read"): exactly
 * these nine members per role.
 *
 * agentAssignable, permissionCount and activeHolderCount are the SERVER's
 * derived values (RA2-RA4). The client shows them; it never computes them, and
 * it never infers one from another.
 */
export const rolesSchema = z.object({
  roles: z.array(
    z.object({
      roleId: z.string().min(1),
      code: z.string().min(1),
      name: z.string().min(1),
      description: z.string().nullable(),
      isSystemRole: z.boolean(),
      isActive: z.boolean(),
      agentAssignable: z.boolean(),
      permissionCount: z.number().int().nonnegative(),
      activeHolderCount: z.number().int().nonnegative(),
    }),
  ),
});

export type Roles = z.infer<typeof rolesSchema>;

export type Role = Roles["roles"][number];

/**
 * AUT-Q3's response: the grants live now. revokedAt is in the contract because
 * an asOf read can carry one; the screen asks only for the current state (RA5),
 * where it is always null.
 */
export const rolePermissionsSchema = z.object({
  permissions: z.array(
    z.object({
      rolePermissionId: z.string().min(1),
      permissionId: z.string().min(1),
      code: z.string().min(1),
      name: z.string().min(1),
      resource: z.string().min(1),
      action: z.string().min(1),
      requiresHumanActor: z.boolean(),
      grantedAt: z.string().min(1),
      revokedAt: z.string().nullable(),
    }),
  ),
});

export type RolePermissions = z.infer<typeof rolePermissionsSchema>;
