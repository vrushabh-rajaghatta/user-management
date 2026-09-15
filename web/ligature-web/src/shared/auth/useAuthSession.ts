import { use, useSyncExternalStore } from "react";
import { AuthSessionContext } from "./authSessionContext";

/**
 * The authentication state, its transitions, and the visibility checks.
 *
 * `can` is obtained here rather than from global state
 * (docs/frontend-architecture.md §9). It reads the session as it is at the
 * moment it is called, so it is safe in an event handler; this hook re-renders
 * the component whenever the session changes.
 */
export function useAuthSession() {
  const context = use(AuthSessionContext);

  if (context === null) {
    throw new Error("useAuthSession is used outside AuthProvider.");
  }

  const { session } = context;
  const state = useSyncExternalStore(session.subscribe, session.getState);

  return {
    state,
    signedIn: context.signedIn,
    signedOut: context.signedOut,
    can: session.can,
    permissionState: session.permissionState,
  };
}
