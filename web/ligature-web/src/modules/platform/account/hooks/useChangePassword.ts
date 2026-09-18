import { useMutation } from "@tanstack/react-query";
import { changePassword } from "../api/changePassword";

/**
 * CRD-C4. Nothing in the query cache changes on success: /me describes the
 * same caller, with the same session, as before.
 */
export function useChangePassword() {
  return useMutation({ mutationFn: changePassword });
}
