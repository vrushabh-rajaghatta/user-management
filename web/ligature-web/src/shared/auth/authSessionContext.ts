import { createContext } from "react";
import type { AuthSession, AuthState } from "./AuthSession";

export interface AuthSessionContextValue {
  readonly session: AuthSession;

  /**
   * Sign-in succeeded. It resolves the session before reporting a state, so the
   * caller is established by the server rather than assumed (§8).
   */
  readonly signedIn: () => Promise<AuthState>;

  /** The session ended: tells the source and clears the query cache. */
  readonly signedOut: () => void;

  /**
   * Resolve the session again after a failed resolution
   * (docs/frontend-architecture.md §8). It asks; it does not sign anyone out.
   */
  readonly retry: () => void;
}

export const AuthSessionContext = createContext<AuthSessionContextValue | null>(null);
