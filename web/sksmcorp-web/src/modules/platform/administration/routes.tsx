import { Navigate, type RouteObject } from "react-router";
import { AreaLayout } from "@/shared/layout/AreaLayout";
import { administrationArea } from "./navigation";

/**
 * The /admin area as a nested layout route (docs/frontend-architecture.md §5).
 * The area's pages come from the modules that own them, passed in by app/, so
 * this module composes the area without reaching into another module's routes.
 *
 * /admin itself redirects to the first page. replace, so Back does not return
 * to a URL that only redirects again.
 */
export function administrationRoutes(pages: RouteObject[]): RouteObject[] {
  return [
    {
      path: "/admin",
      element: <AreaLayout area={administrationArea} />,
      children: [{ index: true, element: <Navigate to="/admin/users" replace /> }, ...pages],
    },
  ];
}
