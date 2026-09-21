import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listUserSessions, revokeSession } from "../api/sessions";
import { userKeys } from "./userKeys";

export function useUserSessions(userId: string) {
  return useQuery({
    queryKey: userKeys.sessions(userId),
    queryFn: ({ signal }) => listUserSessions(userId, signal),
  });
}

/**
 * SES-C3. Settled either way, the user's sessions are read again: after a
 * success the session is gone, and a session that had already ended answers
 * 204 too, so only the server's list says what is left. Nothing else is
 * invalidated: ending one session changes nothing GetUser, the list, Roles or
 * the identities show.
 */
export function useRevokeSession(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: revokeSession,
    onSettled: () => client.invalidateQueries({ queryKey: userKeys.sessions(userId) }),
  });
}
