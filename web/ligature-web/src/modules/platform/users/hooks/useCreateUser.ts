import { useMutation } from "@tanstack/react-query";
import { createUser } from "../api/createUser";

/** The module's public way to create a user (docs/frontend-architecture.md §6). */
export function useCreateUser() {
  return useMutation({ mutationFn: createUser });
}
