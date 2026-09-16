import type { RouteObject } from "react-router";
import { RequirePermission } from "@/shared/auth/RequirePermission";
import { UserPermissions } from "./permissions";

/**
 * Lazy, like every page (docs/frontend-architecture.md §5).
 *
 * GATED, since B6. W4 left this route open for one reason — with no
 * server-backed permission source every permission was permanently unknown, so
 * a gate would have been unreachable and untestable. GET /me removes that
 * reason, and a denial now renders an explicit denied state rather than a blank
 * page or a not-found that would lie about the route existing.
 *
 * This is not the same mechanism as the navigation entry's <Can>, which decides
 * only whether the entry is SHOWN. Neither is access control: the server
 * authorises POST /api/users whatever both of them did.
 */
export const userRoutes: RouteObject[] = [
  // USR-C1.
  {
    path: "/users/new",
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
