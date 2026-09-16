import { useMutation } from "@tanstack/react-query";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import { signIn, type SignInCredentials } from "../api/signIn";
import { SessionNotEndedError, useEndSession } from "./useEndSession";

/**
 * The whole submit sequence, so that "signing in" is one pending state: end any
 * established session, then present the credentials
 * (docs/frontend-architecture.md §13).
 */
export function useSignIn() {
  const endSession = useEndSession();
  const { signedIn } = useAuthSession();

  return useMutation({
    mutationFn: async (credentials: SignInCredentials) => {
      const outcome = await endSession();

      if (!outcome.ended) {
        throw new SessionNotEndedError(outcome.message);
      }

      await signIn(credentials);
    },
    onSuccess: () => {
      signedIn();
    },
  });
}
