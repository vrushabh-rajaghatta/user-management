import { Navigate, Outlet, useLocation } from "react-router";
import { ErrorState } from "@/shared/components/ErrorState";
import { useAuthSession } from "./useAuthSession";

interface RequireAuthProps {
  /** The sign-in route. Passed in by app/, because shared/ cannot know a module's routes. */
  readonly signInPath: string;
}

/**
 * Decides what an authenticated route group renders
 * (docs/frontend-architecture.md §8): nothing while authentication is unknown,
 * the routes when authenticated, a stated failure when resolution could not
 * answer, and a redirect to sign-in when it answered that there is no caller.
 *
 * THE ERROR BRANCH IS NOT A SIGN-OUT. A 5xx, a network failure or a broken
 * response means we do not know whether the session is valid; redirecting to
 * sign-in on that would end a live session because a server had a bad moment.
 * So the failure is stated, the route is held, and the person can ask again.
 *
 * The redirect carries the path and query to come back to — never the fragment,
 * which is where emailed credentials travel. The path is passed on unvalidated;
 * the sign-in flow that follows it must accept only a same-origin relative path.
 *
 * Deciding what to render is not access control. The server still refuses every
 * request this route makes without a live session.
 */
export function RequireAuth({ signInPath }: RequireAuthProps) {
  const { state, retry } = useAuthSession();
  const location = useLocation();

  if (state.status === "unknown") {
    return null;
  }

  if (state.status === "authenticated") {
    return <Outlet />;
  }

  if (state.status === "error") {
    return (
      <div className="mx-auto w-full max-w-xl px-4 py-16 sm:px-6">
        <ErrorState
          title="We could not check your session"
          message="Something went wrong while confirming whether you are signed in. This does not mean you have been signed out."
          onRetry={retry}
        />
      </div>
    );
  }

  return <Navigate to={signInPath} replace state={{ returnTo: `${location.pathname}${location.search}` }} />;
}
