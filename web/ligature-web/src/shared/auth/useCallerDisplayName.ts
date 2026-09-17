import { useQuery } from "@tanstack/react-query";
import { authKeys, me } from "./me";

/**
 * The caller's display name, for presentation (docs/frontend-architecture.md §8).
 *
 * Read from the /me answer caller resolution already holds, NOT added to
 * Principal: Principal is authentication and authorization context, and a name
 * is neither. The query is disabled, so reading the name never asks the server
 * again — /me is not a heartbeat — and when the cache is cleared at sign-out the
 * name goes with it.
 */
export function useCallerDisplayName(): string | undefined {
  const { data } = useQuery({ queryKey: authKeys.me(), queryFn: () => me(), enabled: false });

  return data?.identity.displayName;
}
