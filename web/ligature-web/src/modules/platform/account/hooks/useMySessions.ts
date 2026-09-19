import { useQuery, useQueryClient } from "@tanstack/react-query";
import { listMySessions } from "../api/mySessions";
import { accountKeys } from "./accountKeys";

export function useMySessions() {
  return useQuery({
    queryKey: accountKeys.mySessions,
    queryFn: ({ signal }) => listMySessions(signal),
  });
}

/**
 * Reads the caller's sessions again (MY6), for an action on this page that
 * ended some of them — Sign out other sessions, whose hook belongs to the auth
 * module and knows nothing of this list.
 */
export function useRefreshMySessions() {
  const client = useQueryClient();

  return () => client.invalidateQueries({ queryKey: accountKeys.mySessions });
}
