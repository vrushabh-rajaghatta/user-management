import { useMutation } from "@tanstack/react-query";
import { checkUsernameAvailability } from "../api/usernameAvailability";

/**
 * IDN-Q3, asked when focus leaves Username. A mutation rather than a query:
 * it is asked on an event, never cached, and never retried — a failed check is
 * simply ignored (UN7), and an answer the page no longer wants is discarded by
 * the page (UN6).
 */
export function useUsernameAvailability() {
  return useMutation({ mutationFn: checkUsernameAvailability, retry: false });
}
