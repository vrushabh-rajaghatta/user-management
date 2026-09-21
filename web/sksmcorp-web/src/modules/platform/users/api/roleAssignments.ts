import { api } from "@/shared/api/client";
import { grantableRolesSchema, grantedRoleSchema, roleAssignmentsSchema } from "../schemas/roleAssignments";

/**
 * AUT-Q2: a user's assignments. Requires role.read. History is asked for, never
 * assumed: without it the server returns Active and Future only.
 */
export const listRoleAssignments = (userId: string, includeInactive: boolean, signal?: AbortSignal) =>
  api.get(
    `/api/users/${encodeURIComponent(userId)}/role-assignments${includeInactive ? "?includeInactive=true" : ""}`,
    { response: roleAssignmentsSchema, signal },
  );

/** The active roles that can be granted. Requires role.read. */
export const listGrantableRoles = (signal?: AbortSignal) =>
  api.get("/api/roles", { response: grantableRolesSchema, signal });

/**
 * AUT-C1: POST /api/users/{userId}/role-assignments -> 201 with exactly
 * { userRoleAssignmentId }. Dates are UTC instants; omitted ones are omitted,
 * so the server applies its defaults.
 */
export const grantRole = ({
  userId,
  roleId,
  effectiveFrom,
  effectiveTo,
  reason,
}: {
  userId: string;
  roleId: string;
  effectiveFrom?: string;
  effectiveTo?: string;
  reason: string;
}) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/role-assignments`, {
    body: { roleId, effectiveFrom, effectiveTo, reason },
    response: grantedRoleSchema,
  });

/** AUT-C2: POST /api/role-assignments/{assignmentId}/revoke. 204, no body. */
export const revokeRole = ({ assignmentId, reason }: { assignmentId: string; reason: string }) =>
  api.post(`/api/role-assignments/${encodeURIComponent(assignmentId)}/revoke`, { body: { reason } });
