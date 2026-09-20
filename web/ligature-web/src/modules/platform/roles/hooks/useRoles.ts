import { useQuery } from "@tanstack/react-query";
import { getRolePermissions, listRoles } from "../api/roles";
import { roleKeys } from "./roleKeys";

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
