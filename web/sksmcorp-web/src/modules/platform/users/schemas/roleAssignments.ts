import { z } from "zod";

/**
 * AUT-Q2's state (docs/requirements.md, "AUT-Q2"). Derived by the SERVER at the
 * moment of the read and shown exactly as sent: nothing in the client works a
 * state out from dates, even when the dates seem to disagree.
 */
export const assignmentStateSchema = z.enum(["Active", "Future", "Ended", "Revoked"]);

export type AssignmentState = z.infer<typeof assignmentStateSchema>;

const actorSchema = z.object({
  userId: z.string().min(1),
  displayName: z.string(),
});

/**
 * GET /api/users/{userId}/role-assignments: exactly these fields per row. The
 * provenance — who granted and revoked it, when and why — is what the read is
 * for, so none of it is dropped.
 */
export const roleAssignmentsSchema = z.object({
  assignments: z.array(
    z.object({
      assignmentId: z.string().min(1),
      roleId: z.string().min(1),
      roleName: z.string(),
      effectiveFrom: z.string(),
      effectiveTo: z.string().nullable(),
      state: assignmentStateSchema,
      assignedAt: z.string(),
      assignedBy: actorSchema,
      assignmentReason: z.string(),
      revokedAt: z.string().nullable(),
      revokedBy: actorSchema.nullable(),
      revocationReason: z.string().nullable(),
    }),
  ),
});

export type RoleAssignment = z.infer<typeof roleAssignmentsSchema>["assignments"][number];

/** GET /api/roles: the active roles the grant form offers. Not AUT-Q5. */
export const grantableRolesSchema = z.object({
  roles: z.array(
    z.object({
      roleId: z.string().min(1),
      name: z.string(),
      description: z.string().nullable(),
    }),
  ),
});

export type GrantableRole = z.infer<typeof grantableRolesSchema>["roles"][number];

/** AUT-C1's 201 body: exactly the new assignment's identifier. */
export const grantedRoleSchema = z.object({
  userRoleAssignmentId: z.string().min(1),
});

/** The grant form's affordances; the server decides everything else, overlap included. */
export const grantFormSchema = z.object({
  roleId: z.string().min(1, "Choose a role."),
  reason: z.string().trim().min(1, "A reason is required."),
});
