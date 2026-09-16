import { Navigate, Outlet, useLocation } from "react-router";
import { useAuthSession } from "./useAuthSession";

interface RequireAuthProps {
  /** The sign-in route. Passed in by app/, because shared/ cannot know a module's routes. */
  readonly signInPath: string;
}

/**
 * Decides what an authenticated route group renders
 * (docs/frontend-architecture.md §8): nothing while authentication is unknown,
 * the routes when authenticated, and a redirect to sign-in when not.
 *
 * The redirect carries the path and query to come back to — never the fragment,
 * which is where emailed credentials travel. The path is passed on unvalidated;
 * the sign-in flow that follows it must accept only a same-origin relative path.
 *
 * Deciding what to render is not access control. The server still refuses every
 * request this route makes without a live session.
 */
export function RequireAuth({ signInPath }: RequireAuthProps) {
  const { state } = useAuthSession();
  const location = useLocation();

  if (state.status === "unknown") {
    return null;
  }

  if (state.status === "authenticated") {
    return <Outlet />;
  }

  return <Navigate to={signInPath} replace state={{ returnTo: `${location.pathname}${location.search}` }} />;
}
