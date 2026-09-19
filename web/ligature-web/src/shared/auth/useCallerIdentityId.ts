import { useQuery } from "@tanstack/react-query";
import { authKeys, me } from "./me";

/**
 * The identity THIS SESSION was established with, from the /me answer caller
 * resolution already holds — or undefined when none is held. The query is
 * disabled, so reading it never asks the server again (/me is not a heartbeat).
 *
 * FOR AFFORDANCES ONLY. It lets a page avoid offering an action the server will
 * refuse (the User detail page withholds Unlock on the caller's own user); it
 * is never authorization, and the server decides regardless.
 */
export function useCallerIdentityId(): string | undefined {
  const { data } = useQuery({ queryKey: authKeys.me(), queryFn: () => me(), enabled: false });

  return data?.identity.userIdentityId;
}
