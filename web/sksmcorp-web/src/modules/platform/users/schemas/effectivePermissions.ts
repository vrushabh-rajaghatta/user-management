import { z } from "zod";

/**
 * USR-Q3's response (docs/requirements.md, "USR-Q3 GetUserAccessSummary"):
 * what one person can currently do.
 *
 * NO ROLES (UA3). What was granted is AUT-Q2's; this is what is in effect.
 *
 * `status` is the USER's current status, and is what makes an empty list
 * legible: an Inactive user with nothing is a different fact from an Active
 * user with nothing. It is not a property of the permission set.
 */
export const effectivePermissionsSchema = z.object({
  userId: z.string().min(1),
  status: z.string().min(1),
  permissions: z.array(
    z.object({
      code: z.string().min(1),
      scopeType: z.string().min(1),
      scopeId: z.string().nullable(),
    }),
  ),
});

export type EffectivePermissions = z.infer<typeof effectivePermissionsSchema>;

export type EffectivePermission = EffectivePermissions["permissions"][number];
