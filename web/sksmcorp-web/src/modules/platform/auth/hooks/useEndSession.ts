import { useCallback } from "react";
import { ApiError } from "@/shared/api/errors";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import { signOut } from "../api/signOut";

/**
 * Ending any established session before an identity-establishing request
 * (docs/frontend-architecture.md §13).
 *
 * Sign-in, activation and reset are all refused while a caller is established,
 * so the client ends the session first rather than working around the refusal.
 * It happens on submit, never on page load: opening an emailed link while
 * signed in must change nothing.
 */

/** The sign-out did not settle, so the caller must not go on to establish an identity. */
export class SessionNotEndedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "SessionNotEndedError";
  }
}

export type EndSessionOutcome = { readonly ended: true } | { readonly ended: false; readonly message: string };

const UNKNOWN = "The request could not be completed.";

export function useEndSession(): () => Promise<EndSessionOutcome> {
  const { signedOut } = useAuthSession();

  return useCallback(async (): Promise<EndSessionOutcome> => {
    try {
      await signOut();
    } catch (error) {
      // An abort is not an outcome. It is rethrown untouched, as the API
      // boundary rethrows it.
      if (error instanceof Error && error.name === "AbortError") {
        throw error;
      }

      // 401: there was no live session. The hint was stale or absent, which is
      // exactly the new-tab ambiguity this sequence exists to absorb.
      if (error instanceof ApiError && error.status === 401) {
        signedOut();

        return { ended: true };
      }

      // Anything else leaves it unknown whether a revocation happened, so
      // nothing may be established beside a session that might still be live.
      // Authentication state is deliberately left as it was.
      return { ended: false, message: error instanceof ApiError ? error.message : UNKNOWN };
    }

    signedOut();

    return { ended: true };
  }, [signedOut]);
}
