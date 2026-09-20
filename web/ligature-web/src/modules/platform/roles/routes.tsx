import type { RouteObject } from "react-router";
import { RequirePermission } from "@/shared/auth/RequirePermission";
import { RolePermissions } from "./permissions";

/**
 * Lazy, relative to the Administration area, and each route guards itself, as
 * the users module's do (§5).
 */
export const roleRoutes: RouteObject[] = [
  // AUT-Q5.
  {
    path: "roles",
    lazy: async () => {
      const { RolesPage } = await import("./pages/RolesPage");
      return {
        Component: function GuardedRolesPage() {
          return (
            <RequirePermission permission={RolePermissions.read}>
              <RolesPage />
            </RequirePermission>
          );
        },
      };
    },
  },

  // AUT-Q3, on the role detail page.
  {
    path: "roles/:roleId",
    lazy: async () => {
      const { RoleDetailPage } = await import("./pages/RoleDetailPage");
      return {
        Component: function GuardedRoleDetailPage() {
          return (
            <RequirePermission permission={RolePermissions.read}>
              <RoleDetailPage />
            </RequirePermission>
          );
        },
      };
    },
  },
];
