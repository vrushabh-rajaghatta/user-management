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
 * Deactivate and Reactivate do — see below. Sign out everywhere re-reads only
 * the user's sessions.
 */
export function useResetUserPassword() {
  return useMutation({ mutationFn: resetUserPassword });
}

/**
 * SES-C4, administrator form. It ends the user's sessions, so the User detail
 * page's Active sessions are read again (SS8).
 */
export function useSignOutUserEverywhere() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: signOutUserEverywhere,
    onSuccess: (_result, { userId }) => client.invalidateQueries({ queryKey: userKeys.sessions(userId) }),
  });
}

export function useReissueActivationLink() {
  return useMutation({ mutationFn: reissueActivationLink });
}

/**
 * USR-C4 / USR-C5 change a listed field, so both read again (U7): every page
 * of the list, so the row's status, marker and actions are the server's, and
 * that user's role assignments, which deactivation revoked. Read again, never
 * patched in the cache. Their sessions too (SS8): deactivation ends them, and
 * after a reactivation the re-read is simply unchanged.
 *
 * AND THEIR EFFECTIVE PERMISSIONS (USR-Q3). Deactivation empties the set —
 * the actor gate refuses an inactive user — and, because the section explains
 * an empty set with the user's STATUS, a stale read does not merely show old
 * data: it shows the wrong reason. Found in the browser, where the section
 * still read "No effective permissions." for a user the page had just marked
 * Inactive.
 */
function useLifecycleMutation(mutationFn: (input: { userId: string; reason: string }) => Promise<unknown>) {
  const client = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: (_result, { userId }) =>
      Promise.all([
        client.invalidateQueries({ queryKey: userKeys.lists }),
        client.invalidateQueries({ queryKey: userKeys.roleAssignmentsOf(userId) }),
        client.invalidateQueries({ queryKey: userKeys.sessions(userId) }),
        client.invalidateQueries({ queryKey: userKeys.effectivePermissions(userId) }),
      ]),
  });
}

export function useDeactivateUser() {
  return useLifecycleMutation(deactivateUser);
}

export function useReactivateUser() {
  return useLifecycleMutation(reactivateUser);
}
