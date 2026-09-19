import { useMutation } from "@tanstack/react-query";
import { checkUsernameAvailability } from "../api/usernameAvailability";

// RED-TEST STUB: not used by the page yet.
export function useUsernameAvailability() {
  return useMutation({ mutationFn: checkUsernameAvailability });
}
