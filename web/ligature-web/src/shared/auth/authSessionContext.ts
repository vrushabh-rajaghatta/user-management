import { createContext } from "react";
import type { AuthSession } from "./AuthSession";

export interface AuthSessionContextValue {
  readonly session: AuthSession;

  /** Sign-in succeeded. */
  readonly signedIn: () => void;

  /** The session ended: tells the source and clears the query cache. */
  readonly signedOut: () => void;

  /**
   * Resolve the session again after a failed resolution
   * (docs/frontend-architecture.md §8). It asks; it does not sign anyone out.
   */
  readonly retry: () => void;
}

export const AuthSessionContext = createContext<AuthSessionContextValue | null>(null);
