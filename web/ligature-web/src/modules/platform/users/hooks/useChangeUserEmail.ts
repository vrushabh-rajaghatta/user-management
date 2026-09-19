import { useMutation, useQueryClient } from "@tanstack/react-query";
import { changeUserEmail } from "../api/changeUserEmail";
import { userKeys } from "./userKeys";

/**
 * USR-C3. Every 204 is a save; the client does not predict a no-op. It reads
 * again — never patches — every page of the list, where the address shows,
 * and this user's GetUser.
 */
export function useChangeUserEmail(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: changeUserEmail,
    onSuccess: () =>
      Promise.all([
        client.invalidateQueries({ queryKey: userKeys.lists }),
        client.invalidateQueries({ queryKey: userKeys.profile(userId) }),
      ]),
  });
}
