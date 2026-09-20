import { createBrowserRouter, type RouteObject } from "react-router";
import { MyAccountLink } from "@/modules/platform/account";
import { accountRoutes, myAccountRoutes } from "@/modules/platform/account/routes";
import { SIGN_IN_PATH, SignOutButton } from "@/modules/platform/auth";
import { authRoutes } from "@/modules/platform/auth/routes";
import { homeRoutes } from "@/modules/platform/home/routes";
import { administrationArea } from "@/modules/platform/administration";
import { administrationRoutes } from "@/modules/platform/administration/routes";
import { roleRoutes } from "@/modules/platform/roles/routes";
import { userRoutes } from "@/modules/platform/users/routes";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { AppShell } from "@/shared/layout/AppShell";
import type { NavigationGroup } from "@/shared/layout/navigation";
import { NotFound } from "@/shared/layout/NotFound";
import { PublicShell } from "@/shared/layout/PublicShell";
import { RouteError } from "@/shared/layout/RouteError";

/**
 * Composition only (docs/frontend-architecture.md §5): route groups placed in
 * shells, with the error and not-found routes. This file never lists pages.
 *
 * The error boundary sits inside the shell, so a failing page keeps the header
 * and main landmark around its error.
 *
 * RequireAuth is mounted from W3 onwards, now that a sign-in route exists for
 * its redirect to land on. The not-found route stays OUTSIDE it: an unknown
 * path is not a reason to demand a session.
 */
/** The shell's areas, grouped. Business modules join a "Modules" group when one exists. */
const navigation: readonly NavigationGroup[] = [{ label: "Platform", areas: [administrationArea] }];

/** The caller's own controls, in the shell's footer: each module fills the slot with its own. */
const accountActions = (
  <>
    <MyAccountLink />
    <SignOutButton />
  </>
);

export function composeRoutes(publicRoutes: RouteObject[], privateRoutes: RouteObject[]): RouteObject[] {
  const routes: RouteObject[] = [];

  if (publicRoutes.length > 0) {
    routes.push({
      element: <PublicShell />,
      children: [{ errorElement: <RouteError />, children: publicRoutes }],
    });
  }

  if (privateRoutes.length > 0) {
    routes.push({
      element: <RequireAuth signInPath={SIGN_IN_PATH} />,
      children: [
        {
          element: <AppShell actions={accountActions} navigation={navigation} />,
          children: [{ errorElement: <RouteError />, children: privateRoutes }],
        },
      ],
    });
  }

  routes.push({
    element: <PublicShell />,
    children: [{ errorElement: <RouteError />, children: [{ path: "*", element: <NotFound /> }] }],
  });

  return routes;
}

export const appRoutes = composeRoutes(
  [...authRoutes, ...accountRoutes],
  [...homeRoutes, ...myAccountRoutes, ...administrationRoutes([...userRoutes, ...roleRoutes])],
);

export function createAppRouter() {
  return createBrowserRouter(appRoutes);
}
