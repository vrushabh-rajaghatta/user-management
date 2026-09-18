import type { RouteObject } from "react-router";
import { MY_ACCOUNT_PATH } from "./paths";

/**
 * Lazy, like every page (docs/frontend-architecture.md §5).
 *
 * Two of these paths are part of the BACKEND CONTRACT: NotificationTemplates
 * builds links to /activate and /reset-password, so renaming either breaks
 * every link already sent. /forgot-password is the client's own choice.
 */
export const accountRoutes: RouteObject[] = [
  // CRD-C1. BACKEND CONTRACT PATH — must not be renamed.
  { path: "/activate", lazy: async () => ({ Component: (await import("./pages/ActivatePage")).ActivatePage }) },

  // CRD-C2. Chosen by the client.
  {
    path: "/forgot-password",
    lazy: async () => ({ Component: (await import("./pages/ForgotPasswordPage")).ForgotPasswordPage }),
  },

  // CRD-C3. BACKEND CONTRACT PATH — must not be renamed.
  {
    path: "/reset-password",
    lazy: async () => ({ Component: (await import("./pages/ResetPasswordPage")).ResetPasswordPage }),
  },
];

/**
 * The signed-in half of the module, composed into the application shell behind
 * RequireAuth rather than into the public shell above.
 */
export const myAccountRoutes: RouteObject[] = [
  // CRD-C4 and SES-C4 (self). Chosen by the client.
  {
    path: MY_ACCOUNT_PATH,
    lazy: async () => ({ Component: (await import("./pages/MyAccountPage")).MyAccountPage }),
  },
];
