import type { RouteObject } from "react-router";
import { RequirePermission } from "@/shared/auth/RequirePermission";
import { UserPermissions } from "./permissions";

/**
 * Lazy, like every page (docs/frontend-architecture.md §5).
 *
 * RELATIVE paths: these pages live inside the Administration area, which mounts
 * them under /admin. Neither path is a backend contract.
 *
 * Each route guards itself, and a denial renders an explicit denied state
 * rather than a blank page or a not-found that would lie about the route
 * existing (§5, B6). That is not the same mechanism as the navigation's
 * visibility, and neither is access control: the server authorises every
 * operation whatever both of them did.
 */
export const userRoutes: RouteObject[] = [
  // USR-Q1.
  {
    path: "users",
    lazy: async () => {
      const { UsersPage } = await import("./pages/UsersPage");
      return {
        Component: function GuardedUsersPage() {
          return (
            <RequirePermission permission={UserPermissions.read}>
              <UsersPage />
            </RequirePermission>
          );
        },
      };
    },
  },

  // USR-C1. Moved from /users/new; nothing linked to the old path.
  {
    path: "users/new",
    lazy: async () => {
      const { CreateUserPage } = await import("./pages/CreateUserPage");
      return {
        Component: function GuardedCreateUserPage() {
          return (
            <RequirePermission permission={UserPermissions.create}>
              <CreateUserPage />
            </RequirePermission>
          );
        },
      };
    },
  },
];
