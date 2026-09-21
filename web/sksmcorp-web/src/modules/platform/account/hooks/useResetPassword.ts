import { useMutation } from "@tanstack/react-query";
import { SessionNotEndedError, useEndSession } from "@/modules/platform/auth";
import { resetPassword, type ResetPasswordRequest } from "../api/resetPassword";

/** End any established session, then reset (docs/frontend-architecture.md §13). */
export function useResetPassword() {
  const endSession = useEndSession();

  return useMutation({
    mutationFn: async (request: ResetPasswordRequest) => {
      const outcome = await endSession();

      if (!outcome.ended) {
        throw new SessionNotEndedError(outcome.message);
      }

      return resetPassword(request);
    },
  });
}
