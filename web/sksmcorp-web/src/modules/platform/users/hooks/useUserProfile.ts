import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getUserProfile, updateUserProfile } from "../api/userProfile";
import { userKeys } from "./userKeys";

/** USR-Q1 GetUser v1, for the Edit profile dialog. */
export function useUserProfile(userId: string) {
  return useQuery({
    queryKey: userKeys.profile(userId),
    queryFn: ({ signal }) => getUserProfile(userId, signal),
  });
}

/**
 * USR-C2. Every 204 is a save (U6); the client does not predict a no-op. It
 * reads again — never patches — every page of the list, where the display
 * name shows, and this user's profile.
 */
export function useUpdateUserProfile(userId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: updateUserProfile,
    onSuccess: () =>
      Promise.all([
        client.invalidateQueries({ queryKey: userKeys.lists }),
        client.invalidateQueries({ queryKey: userKeys.profile(userId) }),
      ]),
  });
}
