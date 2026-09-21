import { useMutation, useQueryClient } from "@tanstack/react-query";
import { changePassword } from "../api/changePassword";
import { accountKeys } from "./accountKeys";

/**
 * CRD-C4. /me describes the same caller, with the same session, after a
 * change. The caller's session list does not: a successful change ends every
 * OTHER session (A5), so it is read again (MY6).
 */
export function useChangePassword() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: changePassword,
    onSuccess: () => client.invalidateQueries({ queryKey: accountKeys.mySessions }),
  });
}
