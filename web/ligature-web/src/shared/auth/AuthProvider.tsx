import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { onUnauthorized } from "@/shared/api/client";
import { createAuthSession, type AuthSessionSource } from "./AuthSession";
import { AuthSessionContext } from "./authSessionContext";

interface AuthProviderProps {
  /** Read once, on mount. Switching sources is a change to the composition root. */
  readonly source: AuthSessionSource;
  readonly children: ReactNode;
}

/**
 * Owns the authentication state and every transition of it
 * (docs/frontend-architecture.md §6, §8).
 *
 * The API boundary only reports a 401; this is what acts on the report. It is
 * registered as the boundary's single unauthorized handler: a reported 401 moves
 * the session to unauthenticated and clears the query cache, so the next person
 * on this browser sees nothing of the previous one.
 */
export function AuthProvider({ source, children }: AuthProviderProps) {
  const queryClient = useQueryClient();
  const [session] = useState(() => createAuthSession(source));

  const value = useMemo(
    () => ({
      session,
      signedIn: () => {
        session.signedIn();
      },
      signedOut: () => {
        session.signedOut();
        queryClient.clear();
      },
    }),
    [session, queryClient],
  );

  useEffect(() => {
    void session.start();
  }, [session]);

  useEffect(() => onUnauthorized(value.signedOut), [value]);

  return <AuthSessionContext value={value}>{children}</AuthSessionContext>;
}
