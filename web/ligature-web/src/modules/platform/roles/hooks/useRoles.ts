import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createRole,
  deactivateRole,
  getRolePermissions,
  listRoles,
  reactivateRole,
  updateRoleMetadata,
} from "../api/roles";
import { roleKeys } from "./roleKeys";
import type { UpdateRoleForm } from "../schemas/updateRole";

/** AUT-Q5, for the roles list and for the detail page's metadata. */
export function useRoles(includeInactive: boolean) {
  return useQuery({
    queryKey: roleKeys.list({ includeInactive }),
    queryFn: ({ signal }) => listRoles(includeInactive, signal),
  });
}

/** AUT-Q3, the current state. */
export function useRolePermissions(roleId: string) {
  return useQuery({
    queryKey: roleKeys.grants(roleId),
    queryFn: ({ signal }) => getRolePermissions(roleId, signal),
    retry: false,
  });
}

/**
 * AUT-C3. Every 201 is a creation; the client re-reads the list rather than
 * patching it, so the new row's derived values are the server's.
 */
export function useCreateRole() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: createRole,
    onSuccess: () => client.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

/**
 * AUT-C4. Every 200 is a save, change or not (RM3): the client does not
 * predict a no-op. The role is re-read rather than patched, as a creation is.
 */
export function useUpdateRoleMetadata(roleId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (form: UpdateRoleForm) => updateRoleMetadata(roleId, form),
    onSuccess: () => client.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

/**
 * AUT-C5/C6. Every 200 is a success, transition or not (RD4): the client does
 * not predict a no-op. The role is re-read, which is what flips the action to
 * its inverse.
 */
export function useDeactivateRole(roleId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (reason: string) => deactivateRole(roleId, reason),
    onSuccess: () => client.invalidateQueries({ queryKey: roleKeys.all }),
  });
}

export function useReactivateRole(roleId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: () => reactivateRole(roleId),
    onSuccess: () => client.invalidateQueries({ queryKey: roleKeys.all }),
  });
}
