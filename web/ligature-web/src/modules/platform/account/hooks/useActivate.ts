import { useMutation } from "@tanstack/react-query";
import { SessionNotEndedError, useEndSession } from "@/modules/platform/auth";
import { activate, type ActivateRequest } from "../api/activate";

/** End any established session, then activate (docs/frontend-architecture.md §13). */
export function useActivate() {
  const endSession = useEndSession();

  return useMutation({
    mutationFn: async (request: ActivateRequest) => {
      const outcome = await endSession();

      if (!outcome.ended) {
        throw new SessionNotEndedError(outcome.message);
      }

      return activate(request);
    },
  });
}
