import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listUserIdentities, unlockIdentity } from "../api/identities";
import { userKeys } from "./userKeys";

export function useUserIdentities(userId: string) {
  return useQuery({
    queryKey: userKeys.identities(userId),
    queryFn: ({ signal }) => listUserIdentities(userId, signal),
  });
}

/**
 * CRD-C6. Settled either way, the user's identities are read again: after a
 * success the lock is gone, and after a refusal — "not currently locked" among
 * them, when the lock expired meanwhile — the page must stop offering an action
 * that cannot succeed. Nothing else is invalidated: unlocking changes nothing
 * GetUser, the list or Roles show.
 */
export function useUnlockIdentity(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: unlockIdentity,
    onSettled: () => client.invalidateQueries({ queryKey: userKeys.identities(userId) }),
  });
}
