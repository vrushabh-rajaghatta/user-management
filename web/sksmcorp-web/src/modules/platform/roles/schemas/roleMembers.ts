import { z } from "zod";

/**
 * AUT-Q4's response (docs/requirements.md, "AUT-Q4 GetRoleMembers"): who holds
 * this role at an instant.
 *
 * THE COUNT IS PART OF THIS ANSWER, not read from the roles list. The list
 * derives its own count at its own instant; this one is derived from these
 * very rows, so the two cannot disagree on screen.
 *
 * A row is an ASSIGNMENT, not a person (RH5): assignmentId is the key. There
 * is no revocation, actor type or scope here, because only active holdings are
 * served and V1 has one scope.
 */
export const roleMembersSchema = z.object({
  asOf: z.string().min(1),
  activeHolderCount: z.number().int().nonnegative(),
  members: z.array(
    z.object({
      assignmentId: z.string().min(1),
      userId: z.string().min(1),
      displayName: z.string().min(1),
      /** Nullable because the column is, exactly as the user list's row records. */
      email: z.string().nullable(),
      status: z.string().min(1),
      effectiveFrom: z.string().min(1),
      effectiveTo: z.string().nullable(),
      assignedAt: z.string().min(1),
      assignedBy: z.object({
        userId: z.string().min(1),
        displayName: z.string().min(1),
      }),
      assignmentReason: z.string(),
    }),
  ),
});

export type RoleMembers = z.infer<typeof roleMembersSchema>;

export type RoleMember = RoleMembers["members"][number];
