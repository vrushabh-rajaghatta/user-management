import type { RouteObject } from "react-router";
import { SIGN_IN_PATH } from "./paths";

/**
 * Lazy, like every page (docs/frontend-architecture.md §5). These paths are the
 * client's own choice; the account module's two are the backend's.
 */
export const authRoutes: RouteObject[] = [
  // SES-C1.
  { path: SIGN_IN_PATH, lazy: async () => ({ Component: (await import("./pages/SignInPage")).SignInPage }) },
];
