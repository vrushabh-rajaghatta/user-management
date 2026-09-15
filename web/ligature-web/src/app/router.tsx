import { createBrowserRouter, type RouteObject } from "react-router";
import { homeRoutes } from "@/modules/platform/home/routes";
import { NotFound } from "@/shared/layout/NotFound";
import { PublicShell } from "@/shared/layout/PublicShell";
import { RouteError } from "@/shared/layout/RouteError";

/**
 * Composition only (docs/frontend-architecture.md §5): route groups placed in
 * shells, with the error and not-found routes. This file never lists pages.
 *
 * The error boundary sits inside the shell, so a failing page keeps the header
 * and main landmark around its error. Authenticated routes (RequireAuth and
 * AppShell) are added with the first sign-in route; until then a redirect to
 * sign-in would land on a page that does not exist.
 */
export function composeRoutes(publicRoutes: RouteObject[]): RouteObject[] {
  return [
    {
      element: <PublicShell />,
      children: [
        {
          errorElement: <RouteError />,
          children: [...publicRoutes, { path: "*", element: <NotFound /> }],
        },
      ],
    },
  ];
}

export const appRoutes = composeRoutes(homeRoutes);

export function createAppRouter() {
  return createBrowserRouter(appRoutes);
}
