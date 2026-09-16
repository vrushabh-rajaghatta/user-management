import { useCallback } from "react";
import { useNavigate } from "react-router";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import { signOut } from "../api/signOut";

/**
 * Signing out because the visitor asked to.
 *
 * This is NOT the end-session-first sequence of §13, even though it calls the
 * same endpoint. Here the session ends locally and the page moves on whether or
 * not the call succeeded (docs/frontend-architecture.md §8): the visitor asked
 * to leave, and leaving them on a signed-in page because the host was
 * unreachable would be worse than the cookie outliving the click.
 *
 * Ending a session BEFORE establishing an identity is the opposite: a failure
 * there stops everything, because a second session must not be established
 * beside one that may still be live.
 */
export function useSignOut(signInPath: string): () => Promise<void> {
  const { signedOut } = useAuthSession();
  const navigate = useNavigate();

  return useCallback(async () => {
    await signOut().catch(() => undefined);

    signedOut();

    await navigate(signInPath, { replace: true });
  }, [signedOut, navigate, signInPath]);
}
