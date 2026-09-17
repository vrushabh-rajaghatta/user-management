import { useMutation } from "@tanstack/react-query";
import { resetUserPassword } from "../api/resetUserPassword";
import { signOutUserEverywhere } from "../api/signOutUserEverywhere";

/**
 * The two row actions. Neither invalidates the list: neither changes a field
 * the list shows.
 */
export function useResetUserPassword() {
  return useMutation({ mutationFn: resetUserPassword });
}

export function useSignOutUserEverywhere() {
  return useMutation({ mutationFn: signOutUserEverywhere });
}
