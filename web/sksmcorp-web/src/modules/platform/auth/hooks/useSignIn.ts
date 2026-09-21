import { useMutation } from "@tanstack/react-query";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import { signIn, type SignInCredentials } from "../api/signIn";
import { SessionNotEndedError, useEndSession } from "./useEndSession";

/**
 * The credentials were accepted and the caller could not be established.
 *
 * It is deliberately NOT a sign-in failure: the password was right. It is an
 * authentication-resolution failure, and the difference matters, because
 * entering the application on the strength of the 204 alone would claim an
 * authenticated caller nobody has confirmed.
 */
export class SessionNotResolvedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "SessionNotResolvedError";
  }
}

const UNRESOLVED = "Your session could not be confirmed. Please try again.";

/**
 * The whole submit sequence, so that "signing in" is one pending state
 * (docs/frontend-architecture.md §8, §13):
 *
 *     end any established session -> present the credentials -> resolve the caller
 *
 * The first step is §13: sign-in is refused while a caller is established, so
 * the client ends the session rather than working around the refusal. A 401
 * there means there was nothing to end.
 *
 * The LAST step is §8, and it is not optional. A successful sign-in is an
 * authentication boundary, so the session is resolved from the server before
 * the application is entered. Declaring the session authenticated here instead
 * would leave the principal unknown — and every permission with it — until
 * something else happened to resolve it.
 *
 * Resolution is inside the mutation on purpose: it keeps "signing in" pending
 * until the caller is known, so the sign-in page stays on screen rather than
 * the application rendering without an answer.
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

      const state = await signedIn();

      // Anything but "authenticated" — the server answered 401, failed, or was
      // unreachable — leaves the caller unestablished, and unestablished is not
      // somewhere the application may be entered from.
      if (state.status !== "authenticated") {
        throw new SessionNotResolvedError(UNRESOLVED);
      }
    },
  });
}
