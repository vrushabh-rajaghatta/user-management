import { useMutation, useQuery } from "@tanstack/react-query";
import { listUserSessions, revokeSession } from "../api/sessions";
import { userKeys } from "./userKeys";

export function useUserSessions(userId: string) {
  return useQuery({
    queryKey: userKeys.sessions(userId),
    queryFn: ({ signal }) => listUserSessions(userId, signal),
  });
}

// RED-TEST STUB: re-reads nothing.
// eslint-disable-next-line @typescript-eslint/no-unused-vars -- red-test stub
export function useRevokeSession(_userId: string) {
  return useMutation({ mutationFn: revokeSession });
}
