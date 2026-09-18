import { useMutation } from "@tanstack/react-query";
import { reissueActivationLink } from "../api/reissueActivationLink";
import { resetUserPassword } from "../api/resetUserPassword";
import { signOutUserEverywhere } from "../api/signOutUserEverywhere";

/**
 * The row actions. None invalidates the list: none changes a field the list
 * shows. Reissuing an activation link leaves the user pending.
 */
export function useResetUserPassword() {
  return useMutation({ mutationFn: resetUserPassword });
}

export function useSignOutUserEverywhere() {
  return useMutation({ mutationFn: signOutUserEverywhere });
}

export function useReissueActivationLink() {
  return useMutation({ mutationFn: reissueActivationLink });
}
