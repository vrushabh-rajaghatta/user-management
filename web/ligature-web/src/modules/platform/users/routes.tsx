import type { RouteObject } from "react-router";

/**
 * Lazy, like every page (docs/frontend-architecture.md §5).
 *
 * NOT wrapped in <Can>. That hides navigation, which is presentation; a hidden
 * route would render a blank page, which is not a defined outcome and would be
 * an authorization decision the client is not entitled to make. The server
 * authorises POST /api/users either way.
 */
export const userRoutes: RouteObject[] = [
  // USR-C1.
  { path: "/users/new", lazy: async () => ({ Component: (await import("./pages/CreateUserPage")).CreateUserPage }) },
];
