import { useQuery } from "@tanstack/react-query";
import { getEffectivePermissions } from "../api/effectivePermissions";
import { userKeys } from "./userKeys";

/**
 * USR-Q3. Its own query, so a failure here leaves the rest of the page
 * standing; retry is off because a refusal is an answer.
 */
export function useEffectivePermissions(userId: string) {
  return useQuery({
    queryKey: userKeys.effectivePermissions(userId),
    queryFn: ({ signal }) => getEffectivePermissions(userId, signal),
    retry: false,
  });
}
