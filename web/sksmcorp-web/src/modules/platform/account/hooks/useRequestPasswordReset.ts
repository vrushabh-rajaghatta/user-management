import { useMutation } from "@tanstack/react-query";
import { requestPasswordReset } from "../api/requestPasswordReset";

/**
 * No session is ended first, unlike the three identity-establishing flows: this
 * establishes no identity and is refused by nothing. Signing out here would end
 * the session of someone who only mistyped their own address.
 */
export function useRequestPasswordReset() {
  return useMutation({ mutationFn: requestPasswordReset });
}
