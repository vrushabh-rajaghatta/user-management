import type { UserIdentity } from "../schemas/identities";

/**
 * Whether Unlock is OFFERED on an identity (docs/requirements.md, "IDN-Q1
 * GetUserIdentities and Unlock on the User detail page", I6): every condition
 * CRD-C6 would check that this page can see. AN AFFORDANCE, NOT AUTHORIZATION —
 * the server decides, and refuses any identity of the caller's own user
 * whatever this returned.
 *
 * "Own page" is judged at the USER level, as the server's rule is: when the
 * caller's session identity is any of this user's identities, no identity of
 * the page is offered.
 */
export function unlockOffered({
  identity,
  userStatus,
  ownPage,
  holdsUnlock,
}: {
  readonly identity: UserIdentity;
  readonly userStatus: "Active" | "Inactive";
  readonly ownPage: boolean;
  readonly holdsUnlock: boolean;
}): boolean {
  return (
    holdsUnlock &&
    !ownPage &&
    userStatus === "Active" &&
    identity.type === "Local" &&
    identity.status === "Active" &&
    identity.locked
  );
}
