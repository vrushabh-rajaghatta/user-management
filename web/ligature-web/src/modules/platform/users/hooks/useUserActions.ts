import { useMutation, useQueryClient } from "@tanstack/react-query";
import { deactivateUser } from "../api/deactivateUser";
import { reactivateUser } from "../api/reactivateUser";
import { reissueActivationLink } from "../api/reissueActivationLink";
import { resetUserPassword } from "../api/resetUserPassword";
import { signOutUserEverywhere } from "../api/signOutUserEverywhere";
import { userKeys } from "./userKeys";

/**
 * The row actions. These three do not invalidate the list: none changes a
 * field it shows, and reissuing an activation link leaves the user pending.
 * Deactivate and Reactivate do — see below.
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

/**
 * USR-C4 / USR-C5 change a listed field, so both read again (U7): every page
 * of the list, so the row's status, marker and actions are the server's, and
 * that user's role assignments, which deactivation revoked. Read again, never
 * patched in the cache.
 */
function useLifecycleMutation(mutationFn: (input: { userId: string; reason: string }) => Promise<unknown>) {
  const client = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: (_result, { userId }) =>
      Promise.all([
        client.invalidateQueries({ queryKey: userKeys.lists }),
        client.invalidateQueries({ queryKey: userKeys.roleAssignmentsOf(userId) }),
      ]),
  });
}

export function useDeactivateUser() {
  return useLifecycleMutation(deactivateUser);
}

export function useReactivateUser() {
  return useLifecycleMutation(reactivateUser);
}
