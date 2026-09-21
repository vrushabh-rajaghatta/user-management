import type { NavigationItem } from "@/shared/layout/navigation";
import { RolePermissions } from "./permissions";

/**
 * What this module contributes to the Administration area (§5). One entry:
 * Roles, the page AUT-Q5 backs, shown to holders of role.read.
 */
export const rolesNavigation: readonly NavigationItem[] = [
  { label: "Roles", to: "/admin/roles", permission: RolePermissions.read },
];
