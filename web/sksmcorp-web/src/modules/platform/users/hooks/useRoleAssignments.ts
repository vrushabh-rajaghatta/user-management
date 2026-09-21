import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { grantRole, listGrantableRoles, listRoleAssignments, revokeRole } from "../api/roleAssignments";
import { userKeys } from "./userKeys";

/** One user's assignments, current or with history, as the server states them. */
export function useRoleAssignments(userId: string, includeInactive: boolean) {
  return useQuery({
    queryKey: userKeys.roleAssignments(userId, { includeInactive }),
    queryFn: ({ signal }) => listRoleAssignments(userId, includeInactive, signal),
  });
}

/** The roles the grant form offers. Asked for only by a caller who can grant. */
export function useGrantableRoles(enabled: boolean) {
  return useQuery({
    queryKey: userKeys.grantableRoles,
    queryFn: ({ signal }) => listGrantableRoles(signal),
    enabled,
  });
}

/**
 * Both commands change the assignments, so each reads them again from the
 * server afterwards. Nothing is manufactured in the client: the new row, and
 * every state, is the server's.
 *
 * AND BOTH CHANGE THE EFFECTIVE SET (USR-Q3), which is the whole point of
 * granting a role: what the user can do is exactly what these commands
 * altered. Invalidated here rather than on the page, so the Users list's
 * Manage roles is covered as well as the detail page's.
 */
function useAssignmentMutation(mutationFn: typeof grantRole | typeof revokeRole, userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        client.invalidateQueries({ queryKey: userKeys.roleAssignmentsOf(userId) }),
        client.invalidateQueries({ queryKey: userKeys.effectivePermissions(userId) }),
      ]),
  });
}

export function useGrantRole(userId: string) {
  return useAssignmentMutation(grantRole, userId);
}

export function useRevokeRole(userId: string) {
  return useAssignmentMutation(revokeRole, userId);
}
