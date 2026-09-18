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
 */
export function useGrantRole(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: grantRole,
    onSuccess: () => client.invalidateQueries({ queryKey: userKeys.roleAssignmentsOf(userId) }),
  });
}

export function useRevokeRole(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: revokeRole,
    onSuccess: () => client.invalidateQueries({ queryKey: userKeys.roleAssignmentsOf(userId) }),
  });
}
