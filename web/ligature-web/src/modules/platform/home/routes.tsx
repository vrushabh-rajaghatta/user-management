import type { RouteObject } from "react-router";

/**
 * The placeholder home page. Lazy, like every page (docs/frontend-architecture.md
 * §5). It carries no requirement ID because it implements none: it says plainly
 * that nothing is available yet.
 */
export const homeRoutes: RouteObject[] = [
  {
    path: "/",
    lazy: async () => ({ Component: (await import("./pages/HomePage")).HomePage }),
  },
];
